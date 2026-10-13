using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

/// <summary>
/// Installs UE4SS folder mods without exposing a partially extracted package to the game.
/// All destructive work is preceded by a private snapshot under the caller-provided backup root.
/// </summary>
public static partial class ModPackageInstaller
{
    private const int MaximumArchiveEntries = 10_000;
    private const long MaximumArchiveBytes = 1_024L * 1_024 * 1_024;
    private const string ManifestFileName = "snapshot.json";
    private const string SnapshotContentDirectory = "content";
    private static readonly HashSet<string> ReservedFolders = new(StringComparer.OrdinalIgnoreCase) { "Keybinds", "shared" };
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Validated ownership information contained in a private installation snapshot.</summary>
    public sealed class ModBackupInfo
    {
        public List<string> AffectedFolders { get; init; } = [];
        public List<string> RestoredFolders { get; init; } = [];
    }

    public static ModInstallResult InstallZip(string win64Path, string zipPath, string backupRoot, string? directFolderName = null, CancellationToken cancellationToken = default)
        => InstallZip(win64Path, zipPath, backupRoot, [], directFolderName, ownershipValidator: null, cancellationToken: cancellationToken);

    /// <summary>Installs a package while removing package folders that belonged to its previous version.</summary>
    public static ModInstallResult InstallZip(string win64Path, string zipPath, string backupRoot, IEnumerable<string> replacementFolderNames, string? directFolderName = null, CancellationToken cancellationToken = default)
        => InstallZip(win64Path, zipPath, backupRoot, replacementFolderNames, directFolderName, ownershipValidator: null, cancellationToken: cancellationToken);

    internal static ModInstallResult InstallZip(string win64Path, string zipPath, string backupRoot, IEnumerable<string> replacementFolderNames, string? directFolderName, Func<IReadOnlyList<string>, IReadOnlyList<string>, string?>? ownershipValidator, CancellationToken cancellationToken)
    {
        if (!File.Exists(zipPath)) return Failure($"The archive was not found: {zipPath}");

        string stagingPath = CreateStagingPath(backupRoot);
        try
        {
            ExtractArchiveToDirectory(zipPath, stagingPath, cancellationToken);
            if (IsModFolder(stagingPath))
            {
                string name = directFolderName ?? Path.GetFileNameWithoutExtension(zipPath);
                if (!IsValidFolderName(name)) return Failure("The archive's direct mod folder name is invalid.");
                WrapDirectModRoot(stagingPath, name);
            }
            return InstallPreparedDirectory(win64Path, stagingPath, backupRoot, replacementFolderNames, allowSourceRoot: false, ownershipValidator, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failure("Installation was cancelled.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
        finally
        {
            DeleteDirectoryIfSafe(stagingPath);
        }
    }

    public static ModInstallResult InstallFolder(string win64Path, string sourcePath, string backupRoot, CancellationToken cancellationToken = default)
        => InstallFolder(win64Path, sourcePath, backupRoot, [], ownershipValidator: null, cancellationToken: cancellationToken);

    /// <summary>Installs a local folder while removing folders that belonged to its previous version.</summary>
    public static ModInstallResult InstallFolder(string win64Path, string sourcePath, string backupRoot, IEnumerable<string> replacementFolderNames, CancellationToken cancellationToken = default)
        => InstallFolder(win64Path, sourcePath, backupRoot, replacementFolderNames, ownershipValidator: null, cancellationToken: cancellationToken);

    internal static ModInstallResult InstallFolder(string win64Path, string sourcePath, string backupRoot, IEnumerable<string> replacementFolderNames, Func<IReadOnlyList<string>, IReadOnlyList<string>, string?>? ownershipValidator, CancellationToken cancellationToken, bool revalidateBeforeMutation = false)
    {
        try
        {
            if (!Directory.Exists(sourcePath)) return Failure($"The mod folder was not found: {sourcePath}");
            EnsureSafeDirectoryTree(sourcePath);
            return InstallPreparedDirectory(win64Path, sourcePath, backupRoot, replacementFolderNames, allowSourceRoot: true, ownershipValidator, cancellationToken, revalidateBeforeMutation);
        }
        catch (OperationCanceledException)
        {
            return Failure("Installation was cancelled.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public static void Uninstall(string win64Path, IEnumerable<string> folderNames)
    {
        string root = GetWin64Path(win64Path);
        List<string> names = ValidateNames(folderNames);
        RemoveFolders(root, names);
        RemoveFromModsTxt(root, names);
    }

    /// <summary>Captures the specified mod folders and their enabled state before an external operation changes them.</summary>
    public static string CreateBackup(string win64Path, IEnumerable<string> folderNames, string backupRoot)
    {
        string root = GetWin64Path(win64Path);
        return CreateSnapshot(root, ValidateNames(folderNames), backupRoot, CancellationToken.None);
    }

    /// <summary>Reads a snapshot manifest after validating that it belongs to this game and backup root.</summary>
    public static ModBackupInfo InspectBackup(string win64Path, string backupPath, string backupRoot)
    {
        string root = GetWin64Path(win64Path);
        string validatedBackup = ValidateBackupPath(backupPath, backupRoot);
        SnapshotManifest manifest = ReadManifest(validatedBackup);
        if (!string.Equals(Path.GetFullPath(manifest.GamePath), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This backup belongs to a different game folder.");
        GetManifestFolderNames(manifest, out List<string> affectedNames, out List<string> restoredNames);
        return new ModBackupInfo { AffectedFolders = affectedNames, RestoredFolders = restoredNames };
    }

    /// <summary>Deletes a no-longer-needed private snapshot. Paths outside the configured backup root are rejected.</summary>
    public static void DeleteBackup(string backupPath, string backupRoot)
    {
        string validated = ValidateBackupPath(backupPath, backupRoot);
        EnsureSafeDirectoryTree(validated);
        Directory.Delete(validated, recursive: true);
    }

    public static ModInstallResult RestoreBackup(string win64Path, string backupPath, string backupRoot, CancellationToken cancellationToken = default)
        => RestoreBackupCore(win64Path, backupPath, backupRoot, restoreFullModsList: false, cancellationToken);

    internal static ModInstallResult RollbackInstall(string win64Path, string backupPath, string backupRoot)
        => RestoreBackupCore(win64Path, backupPath, backupRoot, restoreFullModsList: true, CancellationToken.None);

    private static ModInstallResult RestoreBackupCore(string win64Path, string backupPath, string backupRoot, bool restoreFullModsList, CancellationToken cancellationToken)
    {
        try
        {
            string root = GetWin64Path(win64Path);
            string validatedBackup = ValidateBackupPath(backupPath, backupRoot);
            SnapshotManifest manifest = ReadManifest(validatedBackup);
            if (!string.Equals(Path.GetFullPath(manifest.GamePath), root, StringComparison.OrdinalIgnoreCase)) return Failure("This backup belongs to a different game folder.");
            GetManifestFolderNames(manifest, out List<string> affectedNames, out List<string> restoredNames);

            string? currentBackup = null;
            try
            {
                currentBackup = CreateSnapshot(root, affectedNames, backupRoot, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                GameRuntime.EnsureGameClosed(root);
            }
            catch
            {
                if (currentBackup is not null) DeleteDirectoryIfSafe(currentBackup);
                throw;
            }
            try
            {
                RemoveFolders(root, affectedNames);
                string contentRoot = Path.Combine(validatedBackup, SnapshotContentDirectory);
                foreach (SnapshotFolder folder in manifest.Folders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string source = Path.Combine(contentRoot, folder.StorageName);
                    if (!Directory.Exists(source)) throw new InvalidDataException("The backup is missing mod content.");
                    EnsureSafeDirectoryTree(source);
                    CopyDirectory(source, GetModPath(root, folder.Name, folder.Enabled), cancellationToken);
                }
                RestoreModsTxt(root, manifest, affectedNames, restoreFullModsList);
                return new ModInstallResult { Success = true, Folders = restoredNames, ReplacedFolders = affectedNames, BackupPath = currentBackup };
            }
            catch (Exception ex)
            {
                List<string> errors = [ex.Message];
                try
                {
                    RemoveFolders(root, affectedNames);
                    RestoreSnapshot(root, currentBackup, CancellationToken.None);
                }
                catch (Exception rollbackEx)
                {
                    errors.Add($"Rollback also failed: {rollbackEx.Message}");
                }
                return new ModInstallResult { Errors = errors, ReplacedFolders = affectedNames, BackupPath = currentBackup };
            }
        }
        catch (OperationCanceledException)
        {
            return Failure("Restore was cancelled.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public static void ExtractArchive(string zipPath, string destination, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(destination)) throw new IOException("The extraction destination already exists.");
        ExtractArchiveToDirectory(zipPath, destination, cancellationToken);
    }

    public static bool IsModFolder(string path)
    {
        if (!Directory.Exists(path)) return false;
        try
        {
            EnsureNotReparsePoint(path);
            string scripts = Path.Combine(path, "Scripts");
            string script = Path.Combine(path, "Script", "main.lua");
            string dlls = Path.Combine(path, "dlls");
            return Directory.Exists(scripts) || File.Exists(script) ||
                (Directory.Exists(dlls) && Directory.GetFiles(dlls, "*.dll", SearchOption.TopDirectoryOnly).Length > 0);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static ModInstallResult InstallPreparedDirectory(string win64Path, string preparedPath, string backupRoot, IEnumerable<string> replacementFolderNames, bool allowSourceRoot, Func<IReadOnlyList<string>, IReadOnlyList<string>, string?>? ownershipValidator, CancellationToken cancellationToken, bool revalidateBeforeMutation = false)
    {
        string root = GetWin64Path(win64Path);
        EnsureSafeDirectoryTree(preparedPath);
        List<PreparedMod> mods = FindModRoots(preparedPath, allowSourceRoot);
        if (mods.Count == 0) return Failure("The package does not contain a supported UE4SS mod folder.");

        string? nameError = ValidatePreparedModNames(mods, out List<string> names, out HashSet<string> seenNames);
        if (nameError is not null) return Failure(nameError);
        List<string> targets = GetInstallationTargets(replacementFolderNames, names);

        string? ownershipError = ownershipValidator?.Invoke(names, targets);
        if (!string.IsNullOrWhiteSpace(ownershipError)) return Failure(ownershipError);

        Dictionary<string, bool> wasEnabled = GetExistingStates(root, targets);
        Dictionary<string, string> renamedFrom = FindUnambiguousRenameSources(names, targets, wasEnabled);
        bool allPreviouslyInstalledFoldersAreDisabled = AreAllDisabled(wasEnabled);
        string? backup = null;
        try
        {
            backup = CreateSnapshot(root, targets, backupRoot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            GameRuntime.EnsureGameClosed(root);
            // Creation must still be new after snapshotting; never replace a folder added during preparation.
            if (revalidateBeforeMutation && ownershipValidator?.Invoke(names, targets) is { } conflict)
                throw new IOException(conflict);
        }
        catch
        {
            if (backup is not null) DeleteDirectoryIfSafe(backup);
            throw;
        }
        try
        {
            RemoveFolders(root, targets);
            foreach (PreparedMod mod in mods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool enabled = GetInstallEnabledState(mod.Name, wasEnabled, renamedFrom, allPreviouslyInstalledFoldersAreDisabled);
                string destination = GetModPath(root, mod.Name, enabled);
                CopyDirectory(mod.Path, destination, cancellationToken);
                string configurationSource = renamedFrom.TryGetValue(mod.Name, out string? source) ? source : mod.Name;
                PreserveConfiguration(backup, configurationSource, destination, cancellationToken);
            }
            UpdateModsTxtForInstalledPackage(root, mods, targets, seenNames, wasEnabled, renamedFrom, allPreviouslyInstalledFoldersAreDisabled);
            return new ModInstallResult { Success = true, Folders = names, ReplacedFolders = targets, BackupPath = backup };
        }
        catch (Exception ex)
        {
            List<string> errors = [ex.Message];
            try
            {
                RemoveFolders(root, targets);
                RestoreSnapshot(root, backup, CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                errors.Add($"Rollback also failed: {rollbackEx.Message}");
            }
            return new ModInstallResult { Errors = errors, BackupPath = backup };
        }
    }

    private static string? ValidatePreparedModNames(List<PreparedMod> mods, out List<string> names, out HashSet<string> seenNames)
    {
        names = [];
        seenNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (PreparedMod mod in mods)
        {
            if (!IsValidFolderName(mod.Name)) return $"\"{mod.Name}\" is not a valid mod folder name.";
            if (!seenNames.Add(mod.Name)) return $"The package contains more than one mod named \"{mod.Name}\".";
            names.Add(mod.Name);
        }
        return null;
    }

    private static List<string> GetInstallationTargets(IEnumerable<string> replacementFolderNames, List<string> names)
    {
        List<string> targets = ValidateNames(replacementFolderNames);
        HashSet<string> targetSet = new(targets, StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (targetSet.Add(name)) targets.Add(name);
        }
        return targets;
    }

    private static void UpdateModsTxtForInstalledPackage(string root, List<PreparedMod> mods, List<string> targets,
        HashSet<string> seenNames, Dictionary<string, bool> wasEnabled, Dictionary<string, string> renamedFrom, bool allPreviouslyInstalledFoldersAreDisabled)
    {
        List<string> obsoleteNames = [];
        foreach (string target in targets)
        {
            if (!seenNames.Contains(target)) obsoleteNames.Add(target);
        }
        RemoveFromModsTxt(root, obsoleteNames);
        List<string> enabledNames = [];
        foreach (PreparedMod mod in mods)
        {
            if (GetInstallEnabledState(mod.Name, wasEnabled, renamedFrom, allPreviouslyInstalledFoldersAreDisabled)) enabledNames.Add(mod.Name);
        }
        // Lua entry points need a mods.txt entry even when this is the first mod installed.
        string modsTxt = SafePaths.Child(root, Path.Combine(ModManager.ModsFolderName, "mods.txt"));
        if (enabledNames.Count > 0 && !File.Exists(modsTxt)) File.WriteAllText(modsTxt, string.Empty);
        ModManager.EnableInModsTxt(root, enabledNames);
    }

    private static Dictionary<string, string> FindUnambiguousRenameSources(List<string> packageFolders, List<string> replacementFolders, Dictionary<string, bool> existingStates)
    {
        List<string> oldOnly = [];
        foreach (string name in replacementFolders)
        {
            if (!ContainsIgnoreCase(packageFolders, name) && existingStates.ContainsKey(name)) oldOnly.Add(name);
        }
        List<string> newOnly = [];
        foreach (string name in packageFolders)
            if (!existingStates.ContainsKey(name)) newOnly.Add(name);
        return oldOnly.Count == 1 && newOnly.Count == 1
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [newOnly[0]] = oldOnly[0] }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool AreAllDisabled(Dictionary<string, bool> states)
    {
        if (states.Count == 0) return false;
        foreach (bool enabled in states.Values)
            if (enabled) return false;
        return true;
    }

    private static bool ContainsIgnoreCase(List<string> names, string candidate)
    {
        foreach (string name in names)
            if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool GetInstallEnabledState(string name, Dictionary<string, bool> existingStates, Dictionary<string, string> renamedFrom, bool allPreviouslyInstalledFoldersAreDisabled)
    {
        if (existingStates.TryGetValue(name, out bool enabled)) return enabled;
        if (renamedFrom.TryGetValue(name, out string? oldName) && existingStates.TryGetValue(oldName, out enabled)) return enabled;
        return !allPreviouslyInstalledFoldersAreDisabled;
    }

    private static List<PreparedMod> FindModRoots(string sourcePath, bool allowSourceRoot)
    {
        List<PreparedMod> found = [];
        Stack<string> pending = new();
        pending.Push(sourcePath);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            EnsureNotReparsePoint(current);
            if ((allowSourceRoot || !string.Equals(current, sourcePath, StringComparison.OrdinalIgnoreCase)) && IsModFolder(current))
            {
                string? name = Path.GetFileName(current);
                if (!string.IsNullOrEmpty(name)) found.Add(new PreparedMod(name, current));
                continue;
            }
            foreach (string child in Directory.GetDirectories(current)) pending.Push(child);
        }
        return found;
    }

    private static string CreateSnapshot(string win64Path, List<string> names, string backupRoot, CancellationToken cancellationToken)
    {
        string backup = CreateBackupPath(backupRoot);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(backup, SnapshotContentDirectory));
            SnapshotManifest manifest = new() { GamePath = win64Path };
            manifest.AffectedFolders.AddRange(names);
            string modsTxt = GetModsTxtPath(win64Path);
            if (File.Exists(modsTxt))
            {
                manifest.HadModsTxt = true;
                manifest.ModsTxtBytes = File.ReadAllBytes(modsTxt);
            }

            int index = 0;
            foreach (string name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (bool enabled in new[] { true, false })
                {
                    string source = GetModPath(win64Path, name, enabled);
                    if (!Directory.Exists(source)) continue;
                    EnsureSafeDirectoryTree(source);
                    string storage = $"{index:D3}-{name}";
                    CopyDirectory(source, Path.Combine(backup, SnapshotContentDirectory, storage), cancellationToken);
                    manifest.Folders.Add(new SnapshotFolder { Name = name, Enabled = enabled, StorageName = storage });
                    index++;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(backup, ManifestFileName), JsonSerializer.Serialize(manifest, ModPackageInstallerJsonContext.Default.SnapshotManifest));
            return backup;
        }
        catch
        {
            DeleteDirectoryIfSafe(backup);
            throw;
        }
    }

    private static void RestoreSnapshot(string win64Path, string snapshotPath, CancellationToken cancellationToken)
    {
        SnapshotManifest manifest = ReadManifest(snapshotPath);
        GetManifestFolderNames(manifest, out List<string> names, out _);
        RemoveFolders(win64Path, names);
        string content = Path.Combine(snapshotPath, SnapshotContentDirectory);
        foreach (SnapshotFolder folder in manifest.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = Path.Combine(content, folder.StorageName);
            if (!Directory.Exists(source)) throw new InvalidDataException("Snapshot content is missing.");
            CopyDirectory(source, GetModPath(win64Path, folder.Name, folder.Enabled), cancellationToken);
        }
        RestoreModsTxtExactly(win64Path, manifest);
    }

    private static void GetManifestFolderNames(SnapshotManifest manifest, out List<string> affectedNames, out List<string> restoredNames)
    {
        affectedNames = [];
        restoredNames = [];
        HashSet<string> seenAffectedNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenRestoredNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string affected in manifest.AffectedFolders)
        {
            if (!IsValidFolderName(affected)) throw new InvalidDataException("The backup contains an invalid affected mod folder name.");
            if (seenAffectedNames.Add(affected)) affectedNames.Add(affected);
        }
        foreach (SnapshotFolder folder in manifest.Folders)
        {
            if (!IsValidFolderName(folder.Name) || !IsSafeStorageName(folder.StorageName))
                throw new InvalidDataException("The backup contains an invalid mod folder name or content path.");
            if (seenAffectedNames.Add(folder.Name)) affectedNames.Add(folder.Name);
            if (seenRestoredNames.Add(folder.Name)) restoredNames.Add(folder.Name);
        }
    }

    private static void RestoreModsTxtExactly(string win64Path, SnapshotManifest manifest)
    {
        string modsTxt = GetModsTxtPath(win64Path);
        if (manifest.HadModsTxt)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modsTxt)!);
            AppStorage.WriteAtomically(modsTxt, GetManifestModsTxtBytes(manifest));
        }
        else if (File.Exists(modsTxt))
        {
            File.Delete(modsTxt);
        }
    }

    private static void RestoreModsTxt(string win64Path, SnapshotManifest manifest, List<string> affectedFolders, bool restoreFullList)
    {
        // Immediate transaction rollback restores bytes/existence; historical restore preserves newer unrelated mods.
        if (restoreFullList) RestoreModsTxtExactly(win64Path, manifest);
        else RestoreModsTxtForAffectedFolders(win64Path, manifest, affectedFolders);
    }

    // A user-requested restore should not overwrite entries for other mods installed after the snapshot.
    // Replace only the saved entries for the folders the snapshot owns, preserving all unrelated lines.
    private static void RestoreModsTxtForAffectedFolders(string win64Path, SnapshotManifest manifest, List<string> affectedFolders)
    {
        string modsTxt = GetModsTxtPath(win64Path);
        HashSet<string> affected = new(affectedFolders, StringComparer.OrdinalIgnoreCase);
        bool hadCurrentFile = File.Exists(modsTxt);
        ConfigFileDocument? snapshotDocument = manifest.HadModsTxt
            ? ConfigFileDocument.Load("snapshot-mods.txt", GetManifestModsTxtBytes(manifest))
            : null;
        ConfigFileDocument currentDocument = hadCurrentFile
            ? ConfigFileDocument.Load(modsTxt, File.ReadAllBytes(modsTxt))
            : snapshotDocument ?? ConfigFileDocument.Load(modsTxt, []);
        string currentText = hadCurrentFile ? currentDocument.Text : string.Empty;
        List<string> current = SplitModsTxtLines(currentText, out string newline, out bool hasFinalNewline);
        current.RemoveAll(line => TryGetModsTxtEntryName(line, out string? name) && name is not null && affected.Contains(name));

        if (manifest.HadModsTxt)
        {
            string savedText = snapshotDocument!.Text;
            List<string> savedEntries = GetAffectedModsTxtEntries(savedText, affected, out string savedNewline);
            if (!hadCurrentFile)
            {
                newline = savedNewline;
                hasFinalNewline = savedText.EndsWith("\r\n", StringComparison.Ordinal) || savedText.EndsWith('\n') || savedText.EndsWith('\r');
            }
            int insertionIndex = FindKeybindsInsertionIndex(current);
            current.InsertRange(insertionIndex, savedEntries);
        }

        if (current.Count == 0 && !hadCurrentFile && !manifest.HadModsTxt) return;
        Directory.CreateDirectory(Path.GetDirectoryName(modsTxt)!);
        AppStorage.WriteAtomically(modsTxt, currentDocument.Encode(JoinModsTxtLines(current, newline, hasFinalNewline)));
    }

    private static List<string> GetAffectedModsTxtEntries(string text, HashSet<string> affected, out string newline)
    {
        List<string> savedEntries = [];
        List<string> savedLines = SplitModsTxtLines(text, out newline, out _);
        foreach (string line in savedLines)
        {
            if (TryGetModsTxtEntryName(line, out string? name) && name is not null && affected.Contains(name)) savedEntries.Add(line);
        }
        return savedEntries;
    }

    private static byte[] GetManifestModsTxtBytes(SnapshotManifest manifest)
    {
        if (manifest.ModsTxtBytes is not null) return manifest.ModsTxtBytes;
        return System.Text.Encoding.UTF8.GetBytes(manifest.ModsTxt ?? string.Empty);
    }

    internal static List<string> SplitModsTxtLines(string text, out string newline, out bool hasFinalNewline)
    {
        newline = "\r\n";
        if (text.Contains("\r\n", StringComparison.Ordinal)) newline = "\r\n";
        else if (text.Contains('\n')) newline = "\n";
        else if (text.Contains('\r')) newline = "\r";
        hasFinalNewline = text.EndsWith("\r\n", StringComparison.Ordinal) || text.EndsWith('\n') || text.EndsWith('\r');
        List<string> lines = [];
        int start = 0;
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                index++;
                continue;
            }
            lines.Add(text[start..index]);
            int separatorLength = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            start = index + separatorLength;
            index = start;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    internal static string JoinModsTxtLines(List<string> lines, string newline, bool hasFinalNewline)
    {
        string text = string.Join(newline, lines);
        return hasFinalNewline && lines.Count > 0 ? text + newline : text;
    }

    private static bool TryGetModsTxtEntryName(string line, out string? name)
    {
        name = null;
        if (line.TrimStart().StartsWith(';')) return false;
        int colon = line.IndexOf(':');
        if (colon <= 0 || line.IndexOf(':', colon + 1) >= 0) return false;
        string candidate = line[..colon].Trim();
        if (candidate.Length == 0) return false;
        name = candidate;
        return true;
    }

    private static int FindKeybindsInsertionIndex(List<string> lines)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (TryGetModsTxtEntryName(lines[index], out string? name) && string.Equals(name, "Keybinds", StringComparison.OrdinalIgnoreCase)) return index;
        }
        return lines.Count;
    }

    private static void PreserveConfiguration(string backup, string name, string destination, CancellationToken cancellationToken)
    {
        SnapshotManifest manifest = ReadManifest(backup);
        foreach (SnapshotFolder folder in manifest.Folders)
        {
            if (!string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsSafeStorageName(folder.StorageName)) throw new InvalidDataException("Snapshot has an invalid content path.");
            string oldRoot = Path.Combine(backup, SnapshotContentDirectory, folder.StorageName);
            CopyConfigurationFiles(oldRoot, destination, cancellationToken);
        }
    }

    private static void CopyConfigurationFiles(string oldRoot, string destination, CancellationToken cancellationToken)
    {
        GetSafeDirectoryTree(oldRoot, out _, out List<string> oldFiles);
        foreach (string oldFile in oldFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConfigurationFile(oldFile)) continue;
            string relative = Path.GetRelativePath(oldRoot, oldFile);
            string newFile = Path.Combine(destination, relative);
            string? newParent = Path.GetDirectoryName(newFile);
            if (newParent is null) continue;
            if (Directory.Exists(newParent)) EnsureNotReparsePoint(newParent);
            Directory.CreateDirectory(newParent);
            if (File.Exists(newFile)) EnsureNotReparsePoint(newFile);
            File.Copy(oldFile, newFile, overwrite: true);
        }
    }

    private static Dictionary<string, bool> GetExistingStates(string win64Path, List<string> names)
    {
        Dictionary<string, bool> states = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            bool enabled = Directory.Exists(GetModPath(win64Path, name, true));
            bool disabled = Directory.Exists(GetModPath(win64Path, name, false));
            if (enabled || disabled) states[name] = enabled;
        }
        return states;
    }

    private static void RemoveFolders(string win64Path, List<string> names)
    {
        List<string> targets = GetFolderRemovalTargets(win64Path, names);
        foreach (string target in targets) EnsureSafeDirectoryTree(target);
        foreach (string target in targets) Directory.Delete(target, recursive: true);
    }

    private static List<string> GetFolderRemovalTargets(string win64Path, List<string> names)
    {
        List<string> targets = [];
        foreach (string name in names)
        {
            if (!IsValidFolderName(name)) throw new ArgumentException("Invalid mod folder name.");
            foreach (bool enabled in new[] { true, false })
            {
                string parent = Path.Combine(win64Path, enabled ? ModManager.ModsFolderName : ModManager.DisabledModsFolderName);
                if (Directory.Exists(parent)) EnsureNotReparsePoint(parent);
                string target = GetModPath(win64Path, name, enabled);
                if (Directory.Exists(target)) targets.Add(target);
            }
        }
        return targets;
    }

    private static void RemoveFromModsTxt(string win64Path, List<string> names)
    {
        string modsTxt = GetModsTxtPath(win64Path);
        if (!File.Exists(modsTxt)) return;
        HashSet<string> removed = new(names, StringComparer.OrdinalIgnoreCase);
        ConfigFileDocument document = ConfigFileDocument.Load(modsTxt, File.ReadAllBytes(modsTxt));
        List<string> lines = SplitModsTxtLines(document.Text, out string newline, out bool hasFinalNewline);
        List<string> output = [];
        foreach (string line in lines)
        {
            int colon = line.IndexOf(':');
            string name = colon < 0 ? string.Empty : line[..colon].Trim();
            if (!removed.Contains(name)) output.Add(line);
        }
        if (output.Count != lines.Count)
            AppStorage.WriteAtomically(modsTxt, document.Encode(JoinModsTxtLines(output, newline, hasFinalNewline)));
    }

    private static void ExtractArchiveToDirectory(string zipPath, string destination, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaximumArchiveEntries) throw new InvalidDataException("The archive contains too many files.");
        long totalBytes = 0;
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Dictionary<string, bool> archiveTargets = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumArchiveBytes) throw new InvalidDataException("The archive is too large.");
            ValidateArchivePath(entry.FullName);
            string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar))));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The archive contains a path outside its destination.");
            bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            ValidateUniqueArchiveTarget(target, isDirectory, archiveTargets);
            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream input = entry.Open();
            using FileStream output = File.Create(target);
            CopyStream(input, output, cancellationToken);
        }
    }

    private static void ValidateUniqueArchiveTarget(string target, bool isDirectory, Dictionary<string, bool> archiveTargets)
    {
        if (archiveTargets.ContainsKey(target)) throw new InvalidDataException("The archive contains duplicate file paths.");
        string separator = Path.DirectorySeparatorChar.ToString();
        foreach (var existing in archiveTargets)
        {
            if (!existing.Value && target.StartsWith(existing.Key + separator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The archive contains a file-directory path collision.");
            if (!isDirectory && existing.Key.StartsWith(target + separator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The archive contains a file-directory path collision.");
        }
        archiveTargets.Add(target, isDirectory);
    }

    private static void WrapDirectModRoot(string stagingPath, string folderName)
    {
        string wrapper = Path.Combine(stagingPath, folderName);
        Directory.CreateDirectory(wrapper);
        foreach (string child in Directory.EnumerateFileSystemEntries(stagingPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(child, wrapper, StringComparison.OrdinalIgnoreCase)) continue;
            EnsureNotReparsePoint(child);
            string? name = Path.GetFileName(child);
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("The archive contains an invalid direct mod item.");
            string destination = Path.Combine(wrapper, name);
            if (Directory.Exists(child)) FileSystemRetry.Move(() => Directory.Move(child, destination), Thread.Sleep);
            else FileSystemRetry.Move(() => File.Move(child, destination), Thread.Sleep);
        }
    }

    private static void ValidateArchivePath(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath) || Path.IsPathRooted(entryPath)) throw new InvalidDataException("The archive contains an invalid path.");
        string[] parts = entryPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new InvalidDataException("The archive contains an invalid path.");
        foreach (string part in parts)
        {
            if (part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(':') ||
                part.EndsWith(' ') || part.EndsWith('.') || WindowsDeviceNames.Contains(Path.GetFileNameWithoutExtension(part)))
            {
                throw new InvalidDataException("The archive contains an invalid path.");
            }
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        GetSafeDirectoryTree(source, out List<string> directories, out List<string> files);
        string? destinationParent = Path.GetDirectoryName(destination);
        if (destinationParent is not null && Directory.Exists(destinationParent)) EnsureNotReparsePoint(destinationParent);
        Directory.CreateDirectory(destination);
        foreach (string directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(directory, source, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void EnsureSafeDirectoryTree(string directory)
    {
        GetSafeDirectoryTree(directory, out _, out _);
    }

    // Only enumerate one directory at a time. SearchOption.AllDirectories can enter a junction before callers
    // have a chance to reject it.
    private static void GetSafeDirectoryTree(string directory, out List<string> directories, out List<string> files)
    {
        directories = [];
        files = [];
        Stack<string> pending = new();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            EnsureNotReparsePoint(current);
            directories.Add(current);
            foreach (string child in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureNotReparsePoint(child);
                if (Directory.Exists(child)) pending.Push(child);
                else files.Add(child);
            }
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"Reparse points are not allowed: {path}");
    }

    private static List<string> ValidateNames(IEnumerable<string> names)
    {
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (!IsValidFolderName(name)) throw new ArgumentException($"\"{name}\" is not a mod folder that can be changed.");
            if (seen.Add(name)) result.Add(name);
        }
        return result;
    }

    internal static bool IsValidFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.IsPathRooted(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar) || name.Contains(':') || ReservedFolders.Contains(name) ||
            name.Equals("mods.txt", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(' ') || name.EndsWith('.'))
        {
            return false;
        }

        int extensionStart = name.IndexOf('.');
        string baseName = (extensionStart < 0 ? name : name[..extensionStart]).TrimEnd(' ');
        return !WindowsDeviceNames.Contains(baseName);
    }

    private static bool IsSafeStorageName(string name) => !string.IsNullOrWhiteSpace(name) &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith(' ') && !name.EndsWith('.') &&
        !WindowsDeviceNames.Contains(Path.GetFileNameWithoutExtension(name));

    private static bool IsConfigurationFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".toml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWin64Path(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Game folder not found: {full}");
        EnsureNotReparsePoint(full);
        return full;
    }

    private static string GetModPath(string win64Path, string name, bool enabled)
    {
        EnsureNotReparsePoint(win64Path);
        string parent = Path.Combine(win64Path, enabled ? ModManager.ModsFolderName : ModManager.DisabledModsFolderName);
        if (Directory.Exists(parent)) EnsureNotReparsePoint(parent);
        return Path.Combine(parent, name);
    }

    private static string GetModsTxtPath(string win64Path)
    {
        EnsureNotReparsePoint(win64Path);
        string mods = Path.Combine(win64Path, ModManager.ModsFolderName);
        if (Directory.Exists(mods)) EnsureNotReparsePoint(mods);
        return Path.Combine(mods, "mods.txt");
    }

    private static void CopyStream(Stream input, Stream output, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
        }
    }

    private static string CreateStagingPath(string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        return Path.Combine(Path.GetFullPath(backupRoot), ".staging-" + Guid.NewGuid().ToString("N"));
    }

    private static string CreateBackupPath(string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        string path = Path.Combine(Path.GetFullPath(backupRoot), "snapshot-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ValidateBackupPath(string backupPath, string backupRoot)
    {
        string root = Path.GetFullPath(backupRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(backupPath);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(candidate)) throw new ArgumentException("Backup path must be inside the loader backup directory.");
        EnsureSafeDirectoryTree(candidate);
        return candidate;
    }

    private static SnapshotManifest ReadManifest(string backupPath)
    {
        string manifestPath = Path.Combine(backupPath, ManifestFileName);
        if (!File.Exists(manifestPath)) throw new InvalidDataException("This backup has no manifest.");
        SnapshotManifest? manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), ModPackageInstallerJsonContext.Default.SnapshotManifest);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.GamePath)) throw new InvalidDataException("The backup manifest is invalid.");
        return manifest;
    }

    private static ModInstallResult Failure(string error) => new() { Errors = [error] };

    private static void DeleteDirectoryIfSafe(string path)
    {
        if (!Directory.Exists(path)) return;
        try { EnsureSafeDirectoryTree(path); Directory.Delete(path, recursive: true); }
        catch (IOException) { /* Staging cleanup must not replace the install result or hide its recovery snapshot. */ }
        catch (UnauthorizedAccessException) { /* Staging cleanup must not replace the install result or hide its recovery snapshot. */ }
    }

    private sealed record PreparedMod(string Name, string Path);

    private sealed class SnapshotManifest
    {
        public string GamePath { get; set; } = string.Empty;
        public bool HadModsTxt { get; set; }
        public byte[]? ModsTxtBytes { get; set; }
        public string? ModsTxt { get; set; }
        public List<string> AffectedFolders { get; set; } = [];
        public List<SnapshotFolder> Folders { get; set; } = [];
    }

    private sealed class SnapshotFolder
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string StorageName { get; set; } = string.Empty;
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(SnapshotManifest))]
    private sealed partial class ModPackageInstallerJsonContext : JsonSerializerContext;
}
