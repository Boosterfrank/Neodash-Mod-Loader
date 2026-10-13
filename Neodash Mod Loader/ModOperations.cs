using System.Security.Cryptography;
using System.Text.Json;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

public static class ModOperations
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    public static bool IsBusy { get; private set; }
    public static event EventHandler? Changed;

    public static bool SameGame(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    public static InstalledModRecord? FindRecordForFolder(string folderName)
    {
        string path = GamePathResolver.GetNeodashWin64Path();
        if (path.Length == 0) return null;
        return SettingsManager.Current.Installations.Find(r => SameGame(r.GamePath, path)
            && r.FolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase));
    }

    public static InstalledModRecord? FindRecord(ModLibraryItem mod)
    {
        string path = GamePathResolver.GetNeodashWin64Path();
        if (path.Length == 0) return null;
        return SettingsManager.Current.Installations.Find(r => SameGame(r.GamePath, path)
            && string.Equals(r.Id, mod.Id ?? mod.Name, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> GetInstalledFolders(ModLibraryItem mod)
    {
        string path = GamePathResolver.GetNeodashWin64Path();
        if (path.Length == 0) return [];
        var record = FindRecord(mod);
        var candidates = record?.FolderNames ?? mod.FolderNames ?? [mod.Name, mod.Name.Replace(" ", ""), mod.Name.Replace(" ", "_")];
        // This display name has always been distributed as CustomServer, including before installation records existed.
        if (record is null && mod.Name.Equals("Neodash Custom Server", StringComparison.OrdinalIgnoreCase)) candidates = ["CustomServer"];
        List<string> installed = [];
        string expectedIdentity = mod.Id ?? mod.Name;
        foreach (string folder in candidates)
        {
            InstalledModRecord? owner = FindRecordForFolder(folder);
            if (owner is not null && !string.Equals(owner.Id, expectedIdentity, StringComparison.OrdinalIgnoreCase)) continue;
            if (ModManager.IsModInstalled(path, folder) && !installed.Contains(folder, StringComparer.OrdinalIgnoreCase)) installed.Add(folder);
        }
        return installed;
    }

    public static async Task<ModLibraryResponse> LoadCatalogAsync(CancellationToken token = default)
    {
        string cache = Path.Combine(AppStorage.DirectoryPath, "mod-library.json");
        string json;
        try
        {
            json = await Client.GetStringAsync(Properties.ResourceStrings.GetRequired("ModLibraryUrl"), token);
            var fresh = JsonSerializer.Deserialize(json, AppJsonContext.Default.ModLibraryResponse) ?? throw new JsonException("The mod library was empty.");
            fresh.Validate();
            try { AppStorage.WriteAtomically(cache, json); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppStorage.Log("Could not cache the mod library.", ex); }
            return fresh;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested)
        {
            AppStorage.Log("Using the cached mod library because the online library is unavailable.", ex);
            if (!File.Exists(cache)) throw;
            json = await File.ReadAllTextAsync(cache, token);
        }
        var cached = JsonSerializer.Deserialize(json, AppJsonContext.Default.ModLibraryResponse) ?? throw new JsonException("The cached library was empty.");
        cached.Validate();
        return cached;
    }

    public static Task InstallCatalogAsync(ModLibraryItem mod, IProgress<OperationProgress>? progress = null, CancellationToken token = default) =>
        RunAsync(async path =>
        {
            string download = await DownloadAsync(mod.DownloadURL, ".zip", progress, token, mod.Sha256);
            try
            {
                progress?.Report(new("Validating and installing " + mod.Name));
                GameRuntime.EnsureGameClosed(path);
                var previous = FindRecord(mod);
                string identity = mod.Id ?? mod.Name;
                string directName = mod.FolderNames is { Count: 1 } ? mod.FolderNames[0] : mod.Name.Replace(" ", "");
                var result = await Task.Run(() => ModPackageInstaller.InstallZip(path, download, AppStorage.BackupsPath, previous?.FolderNames ?? [], directName,
                    (_, affectedFolders) => FindOwnershipConflict(path, identity, affectedFolders), token), token);
                EnsureSuccess(result, token);
                await SaveInstallationAsync(path, result, new InstalledModRecord {
                    Id = identity, Name = mod.Name, Version = mod.Version, GamePath = path,
                    FolderNames = result.Folders, DownloadUrl = mod.DownloadURL, Sha256 = mod.Sha256,
                    BackupPath = result.BackupPath, BackupVersion = previous?.Version ?? SettingsManager.Current.GetInstalledModVersion(mod.Name)
                });
            }
            finally { DeleteDownload(download); }
        }, token);

    public static Task ImportZipAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) =>
        ImportAsync(path, false, progress, token);

    public static Task ImportFolderAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) =>
        ImportAsync(path, true, progress, token);

    private static Task ImportAsync(string source, bool folder, IProgress<OperationProgress>? progress, CancellationToken token) =>
        RunAsync(async path =>
        {
            progress?.Report(new("Validating local mod package…"));
            var result = await Task.Run(() => folder
                ? ModPackageInstaller.InstallFolder(path, source, AppStorage.BackupsPath, [],
                    (packageFolders, affectedFolders) => FindOwnershipConflict(path, CanonicalLocalId(packageFolders), affectedFolders), token)
                : ModPackageInstaller.InstallZip(path, source, AppStorage.BackupsPath, [], directFolderName: null,
                    ownershipValidator: (packageFolders, affectedFolders) => FindOwnershipConflict(path, CanonicalLocalId(packageFolders), affectedFolders), cancellationToken: token), token);
            EnsureSuccess(result, token);
            // A local package has no reliable online version or download source.
            await SaveInstallationAsync(path, result, new InstalledModRecord {
                Id = CanonicalLocalId(result.Folders), Name = string.Join(", ", result.Folders),
                GamePath = path, FolderNames = result.Folders, Version = "local", BackupPath = result.BackupPath,
                BackupVersion = FindRecordForFolder(result.Folders[0])?.Version
            });
        }, token);

    public static Task UninstallFolderAsync(string folderName) => UninstallFoldersAsync(FindRecordForFolder(folderName)?.FolderNames ?? [folderName]);

    /// <summary>Uninstalls a catalog item only if every tracked target still belongs to that catalog identity.</summary>
    public static Task UninstallCatalogAsync(ModLibraryItem mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        string identity = mod.Id ?? mod.Name;
        InstalledModRecord? record = FindRecord(mod);
        return UninstallFoldersAsync(record?.FolderNames ?? GetInstalledFolders(mod), identity);
    }

    public static Task UninstallFoldersAsync(IEnumerable<string> folderNames, string? expectedIdentity = null) => RunAsync(async path =>
    {
        List<string> folders = folderNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count == 0) throw new InvalidOperationException("No mod folders were selected for removal.");
        if (expectedIdentity is not null) EnsureExpectedUninstallOwnership(path, folders, expectedIdentity);
        var settingsBefore = SettingsManager.CaptureSnapshot();
        string backup = await Task.Run(() => ModPackageInstaller.CreateBackup(path, folders, AppStorage.BackupsPath));
        try { GameRuntime.EnsureGameClosed(path); }
        catch
        {
            PruneBackup(backup, null);
            throw;
        }
        List<string> supersededBackups = [];
        try
        {
            await Task.Run(() => ModPackageInstaller.Uninstall(path, folders));
            RemoveUninstalledFolderRecords(path, folders, supersededBackups);
            SettingsManager.SaveSettings();
            ProfileManager.RemoveMods(folders, path);
        }
        catch (Exception original)
        {
            await RecoverFailedOperationAsync(async () =>
                EnsureSuccess(await Task.Run(() => ModPackageInstaller.RestoreBackup(path, backup, AppStorage.BackupsPath)), CancellationToken.None),
                settingsBefore, original, "Removal failed; recovery backup: " + backup);
            throw;
        }
        AppStorage.Log("Uninstalled and deleted: " + string.Join(", ", folders));
        PruneBackup(backup, null);
        foreach (string superseded in supersededBackups) PruneBackup(superseded, null);
    }, CancellationToken.None);

    private static void RemoveUninstalledFolderRecords(string path, List<string> folders, List<string> supersededBackups)
    {
        foreach (var item in SettingsManager.Current.Installations.ToArray())
        {
            if (!SameGame(item.GamePath, path) || !item.FolderNames.Exists(f => folders.Contains(f, StringComparer.OrdinalIgnoreCase))) continue;
            item.FolderNames.RemoveAll(f => folders.Contains(f, StringComparer.OrdinalIgnoreCase));
            if (item.BackupPath is not null) supersededBackups.Add(item.BackupPath);
            item.BackupPath = null;
            item.BackupVersion = null;
            if (item.FolderNames.Count == 0)
            {
                SettingsManager.Current.Installations.Remove(item);
                SettingsManager.Current.RemoveInstalledModVersion(item.Name);
            }
        }
        foreach (string folder in folders) SettingsManager.Current.RemoveInstalledModVersion(folder);
        if (folders.Contains("CustomServer", StringComparer.OrdinalIgnoreCase)) SettingsManager.Current.RemoveInstalledModVersion("Neodash Custom Server");
    }

    public static async Task ReinstallFolderAsync(string folderName, IProgress<OperationProgress>? progress = null, CancellationToken token = default)
    {
        var record = FindRecordForFolder(folderName);
        if (record?.DownloadUrl is null)
        {
            var catalog = await LoadCatalogAsync(token);
            var mod = catalog.Mods?.Find(m => GetInstalledFolders(m).Contains(folderName, StringComparer.OrdinalIgnoreCase));
            if (mod is null) throw new InvalidOperationException("This local mod has no download source. Import its ZIP or folder again to repair it.");
            await InstallCatalogAsync(mod, progress, token);
        }
        else
        {
            await InstallCatalogAsync(new ModLibraryItem { Id = record.Id, Name = record.Name, Version = record.Version,
                DownloadURL = record.DownloadUrl, Sha256 = record.Sha256, FolderNames = record.FolderNames }, progress, token);
        }
    }

    public static Task RestoreFolderAsync(string folderName) => RunAsync(async path =>
    {
        var record = FindRecordForFolder(folderName);
        if (record?.BackupPath is null) throw new InvalidOperationException("No update backup is available for this mod.");
        string currentVersion = record.Version;
        string oldBackup = record.BackupPath;
        List<string> previousFolders = [.. record.FolderNames];
        var before = SettingsManager.CaptureSnapshot();
        EnsureBackupCanRestore(path, record, ModPackageInstaller.InspectBackup(path, oldBackup, AppStorage.BackupsPath));
        var result = await Task.Run(() => ModPackageInstaller.RestoreBackup(path, oldBackup, AppStorage.BackupsPath));
        EnsureSuccess(result, CancellationToken.None);
        UpdateRecordAfterRestore(record, result, currentVersion);
        try { SettingsManager.SaveSettings(); ProfileManager.ReconcilePackageFoldersAndSync(path, previousFolders, record.FolderNames); }
        catch (Exception original)
        {
            await RecoverFailedOperationAsync(async () =>
            {
                if (result.BackupPath is not null) EnsureSuccess(await Task.Run(() => ModPackageInstaller.RestoreBackup(path, result.BackupPath, AppStorage.BackupsPath)), CancellationToken.None);
            }, before, original, "Restore failed; recovery backup: " + result.BackupPath);
            throw;
        }
        if (result.Folders.Count == 0)
        {
            PruneBackup(oldBackup, null);
            if (result.BackupPath is not null) PruneBackup(result.BackupPath, null);
        }
        else PruneBackup(oldBackup, result.BackupPath);
    }, CancellationToken.None);

    private static void UpdateRecordAfterRestore(InstalledModRecord record, ModInstallResult result, string currentVersion)
    {
        record.Version = record.BackupVersion ?? "unknown";
        record.FolderNames = result.Folders;
        record.BackupVersion = currentVersion;
        record.BackupPath = result.BackupPath;
        if (record.FolderNames.Count == 0)
        {
            SettingsManager.Current.Installations.Remove(record);
            SettingsManager.Current.RemoveInstalledModVersion(record.Name);
        }
        else SettingsManager.Current.SetInstalledModVersion(record.Name, record.Version);
    }

    public static async Task RunAsync(Func<string, Task> operation, CancellationToken token, bool requiresProfiles = true)
    {
        await Gate.WaitAsync(token);
        try
        {
            IsBusy = true;
            string path = GamePathResolver.RequireGamePath();
            SettingsManager.EnsureWritable();
            if (requiresProfiles)
            {
                ProfileManager.EnsureWritable();
                ProfileManager.EnsureProfileGamePath(ProfileManager.ActiveProfile, path);
                ProfileManager.BindActiveProfile(path);
            }
            SettingsManager.SaveSettings();
            GameRuntime.EnsureGameClosed(path);
            await operation(path);
            AppStorage.Log("Mod operation completed.");
        }
        catch (Exception ex) { AppStorage.Log("Mod operation did not complete.", ex); throw; }
        finally { IsBusy = false; Gate.Release(); Changed?.Invoke(null, EventArgs.Empty); }
    }

    private static async Task SaveInstallationAsync(string path, ModInstallResult result, InstalledModRecord record)
    {
        var original = SettingsManager.CaptureSnapshot();
        List<string> supersededBackups = [];
        List<string> previousFolders = ReplaceInstallationRecord(path, result, record, supersededBackups);
        try { SettingsManager.SaveSettings(); ProfileManager.ReconcilePackageFoldersAndSync(path, previousFolders, record.FolderNames); }
        catch (Exception originalFailure)
        {
            await RecoverFailedOperationAsync(async () =>
            {
                if (result.BackupPath is not null)
                    EnsureSuccess(await Task.Run(() => ModPackageInstaller.RestoreBackup(path, result.BackupPath, AppStorage.BackupsPath)), CancellationToken.None);
                else await Task.Run(() => ModPackageInstaller.Uninstall(path, result.Folders));
            }, original, originalFailure, "Install failed; recovery backup: " + result.BackupPath);
            throw;
        }
        AppStorage.Log("Installed " + record.Name + " " + record.Version);
        foreach (string superseded in supersededBackups) PruneBackup(superseded, record.BackupPath);
    }

    private static List<string> ReplaceInstallationRecord(string path, ModInstallResult result, InstalledModRecord record, List<string> supersededBackups)
    {
        var records = SettingsManager.Current.Installations;
        List<string> previousFolders = [];
        foreach (var previous in records.ToArray())
        {
            if (!SameGame(previous.GamePath, path)) continue;
            if (previous.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase))
            {
                previousFolders.AddRange(previous.FolderNames);
                if (previous.BackupPath is not null) supersededBackups.Add(previous.BackupPath);
                records.Remove(previous);
                SettingsManager.Current.RemoveInstalledModVersion(previous.Name);
            }
            else
            {
                RemoveReplacedFoldersFromRecord(previous, result.ReplacedFolders, records, supersededBackups);
            }
        }
        records.Add(record);
        SettingsManager.Current.SetInstalledModVersion(record.Name, record.Version);
        return previousFolders;
    }

    private static void RemoveReplacedFoldersFromRecord(InstalledModRecord previous, List<string> replacedFolders,
        List<InstalledModRecord> records, List<string> supersededBackups)
    {
        int removed = previous.FolderNames.RemoveAll(f => replacedFolders.Contains(f, StringComparer.OrdinalIgnoreCase));
        if (removed > 0 && previous.BackupPath is not null) supersededBackups.Add(previous.BackupPath);
        if (removed > 0)
        {
            previous.BackupPath = null;
            previous.BackupVersion = null;
        }
        if (previous.FolderNames.Count == 0)
        {
            records.Remove(previous);
            SettingsManager.Current.RemoveInstalledModVersion(previous.Name);
        }
    }

    private static async Task RecoverFailedOperationAsync(Func<Task> restoreFiles, AppSettings settingsBefore,
        Exception originalFailure, string message)
    {
        List<Exception> errors = [originalFailure];
        try { await restoreFiles(); }
        catch (Exception ex) { errors.Add(ex); }
        try { SettingsManager.RestoreSnapshot(settingsBefore); }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 1) throw new AggregateException(message, errors);
    }

    private static void EnsureBackupCanRestore(string path, InstalledModRecord record, ModPackageInstaller.ModBackupInfo backup)
    {
        foreach (InstalledModRecord other in SettingsManager.Current.Installations)
        {
            if (ReferenceEquals(other, record) || !SameGame(other.GamePath, path)) continue;
            foreach (string ownedFolder in other.FolderNames)
            {
                if (backup.AffectedFolders.Contains(ownedFolder, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"This backup would overwrite folder '{ownedFolder}', which is now owned by {other.Name}.");
            }
        }
    }

    private static string? FindOwnershipConflict(string path, string identity, IReadOnlyList<string> incomingFolders)
    {
        foreach (InstalledModRecord other in SettingsManager.Current.Installations)
        {
            if (!SameGame(other.GamePath, path) || !FoldersOverlap(other.FolderNames, incomingFolders)) continue;
            if (string.Equals(other.Id, identity, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsLocalIdentity(other.Id) && IsLocalIdentity(identity) && HaveSameFolderSet(other.FolderNames, incomingFolders)) continue;
            return $"A folder in this package is already owned by {other.Name}. Uninstall that package before replacing it.";
        }
        return null;
    }

    private static string CanonicalLocalId(IEnumerable<string> folders)
    {
        List<string> ordered = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ordered.Sort(StringComparer.OrdinalIgnoreCase);
        return "local:" + string.Join("|", ordered);
    }

    private static bool IsLocalIdentity(string identity) => identity.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    private static bool FoldersOverlap(IEnumerable<string> first, IReadOnlyList<string> second)
    {
        foreach (string folder in first)
            if (second.Contains(folder, StringComparer.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool HaveSameFolderSet(IEnumerable<string> first, IReadOnlyList<string> second)
    {
        HashSet<string> firstSet = new(first, StringComparer.OrdinalIgnoreCase);
        HashSet<string> secondSet = new(second, StringComparer.OrdinalIgnoreCase);
        return firstSet.SetEquals(secondSet);
    }

    private static void EnsureExpectedUninstallOwnership(string path, IReadOnlyList<string> folders, string expectedIdentity)
    {
        foreach (string folder in folders)
        {
            InstalledModRecord? owner = SettingsManager.Current.Installations.Find(record => SameGame(record.GamePath, path)
                && record.FolderNames.Contains(folder, StringComparer.OrdinalIgnoreCase));
            if (owner is not null && !string.Equals(owner.Id, expectedIdentity, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Folder '{folder}' is now owned by {owner.Name}; refresh the mod library before uninstalling.");
        }
    }

    public static async Task<string> DownloadAsync(string url, string extension, IProgress<OperationProgress>? progress, CancellationToken token, string? sha256 = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Downloads must use a valid HTTPS address.");
        Directory.CreateDirectory(AppStorage.DownloadsPath);
        string path = Path.Combine(AppStorage.DownloadsPath, Guid.NewGuid().ToString("N") + extension);
        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            if (length > 1_073_741_824) throw new InvalidDataException("The download is larger than the supported 1 GB limit.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            byte[] buffer = new byte[81920];
            long total = 0;
            int count;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                total += count;
                if (total > 1_073_741_824) throw new InvalidDataException("The download exceeded the supported 1 GB limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                hash.AppendData(buffer.AsSpan(0, count));
                progress?.Report(new($"Downloading… {total / 1048576.0:F1} MB", length is > 0 ? total * 100.0 / length.Value : null));
            }
            if (!string.IsNullOrWhiteSpace(sha256) && !Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download checksum did not match the mod library. No game files were changed.");
            return path;
        }
        catch { DeleteDownload(path); throw; }
    }

    public static void DeleteDownload(string path)
    {
        string target = SafePaths.Child(AppStorage.DownloadsPath, Path.GetRelativePath(AppStorage.DownloadsPath, path));
        if (File.Exists(target)) File.Delete(target);
    }

    public static void EnsureSuccess(ModInstallResult result, CancellationToken token)
    {
        if (result.Success) return;
        if (result.Errors.Count > 1)
            throw new IOException(string.Join("\n", result.Errors) + (result.BackupPath is null ? "" : "\nRecovery backup: " + result.BackupPath));
        token.ThrowIfCancellationRequested();
        throw new IOException(string.Join("\n", result.Errors));
    }

    private static void PruneBackup(string path, string? keep)
    {
        if (string.Equals(path, keep, StringComparison.OrdinalIgnoreCase)) return;
        try { ModPackageInstaller.DeleteBackup(path, AppStorage.BackupsPath); }
        catch (Exception ex) { AppStorage.Log("Could not remove a superseded backup.", ex); }
    }
}
