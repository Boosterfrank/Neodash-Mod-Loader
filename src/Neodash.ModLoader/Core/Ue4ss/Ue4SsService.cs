using System.Diagnostics;
using System.Text.RegularExpressions;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

public static partial class Ue4SsService
{
    private const string RuntimeDllName = "UE4SS.dll";
    private const string ProxyDllName = "dwmapi.dll";
    private const int MaximumLogHeaderCharacters = 16_384;

    [GeneratedRegex(@"^(?:\[[0-9][0-9:., -]*\][ \t]+)?UE4SS - v(?<version>[0-9]+(?:\.[0-9]+){2,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?(?:[ \t]+[A-Za-z][A-Za-z0-9]*(?:[ \t]+#?[0-9?]+)?)*)(?:[ \t]+- Git SHA #[0-9A-Za-z]+)?[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex StartupVersionPattern();

    public static string GetStatus(string path)
    {
        if (path.Length == 0) return "Choose your Neodash folder first.";
        string? dll = FindRuntimeDll(path);
        if (dll is null) return "UE4SS is not installed.";
        string runtime = "UE4SS installed";
        string? version = ReadRuntimeVersion(dll);
        if (version is not null) runtime = $"UE4SS {version}";
        else if (ReadLastRunVersion(dll) is { } lastRunVersion) runtime = $"UE4SS {lastRunVersion} (last run)";
        bool proxy = File.Exists(Path.Combine(path, ProxyDllName));
        return $"{runtime} — {(proxy ? "loader files present" : "proxy DLL missing; repair recommended")}";
    }

    public static string GetLogPath(string path)
    {
        string? dll = FindRuntimeDll(path);
        return dll is null ? Path.Combine(path, "UE4SS.log") : Path.ChangeExtension(dll, ".log");
    }

    public static bool CanUninstall(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var record = SettingsManager.Current.Ue4ssInstallations.Find(r => ModOperations.SameGame(r.GamePath, path));
        if (record is null) return false;
        try { return GetRuntimeTargets(path, record).Any(relative => File.Exists(SafePaths.Child(path, relative))); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string? FindRuntimeDll(string path)
    {
        // Current UE4SS proxies prefer the ue4ss subfolder over the legacy flat layout.
        foreach (string directory in new[] { Path.Combine(path, "ue4ss"), path })
        {
            string dll = Path.Combine(directory, RuntimeDllName);
            if (File.Exists(dll)) return dll;
        }
        return null;
    }

    private static string? ReadRuntimeVersion(string dll)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dll);
            if (!string.IsNullOrWhiteSpace(info.FileVersion)) return info.FileVersion.Trim();
            if (!string.IsNullOrWhiteSpace(info.ProductVersion)) return info.ProductVersion.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            AppStorage.Log("Could not read the UE4SS version.", ex);
        }
        return null;
    }

    private static string? ReadLastRunVersion(string dll)
    {
        string log = Path.Combine(Path.GetDirectoryName(dll)!, "UE4SS.log");
        try
        {
            if (!File.Exists(log) || File.GetLastWriteTimeUtc(log) < File.GetLastWriteTimeUtc(dll)) return null;
            // Read only the startup header; logs may be large or still in use by the game.
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            char[] buffer = new char[MaximumLogHeaderCharacters];
            int count = reader.ReadBlock(buffer, 0, buffer.Length);
            // A header cut off by the read limit must not look like a complete release version.
            if (count == buffer.Length) count = Array.LastIndexOf(buffer, '\n') + 1;
            Match match = StartupVersionPattern().Match(new string(buffer, 0, count));
            return match.Success ? match.Groups["version"].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            AppStorage.Log("Could not read the UE4SS startup version.", ex);
            return null;
        }
    }

    public static Task InstallAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default) =>
        ModOperations.RunAsync(async path =>
        {
            string archive = await ModOperations.DownloadAsync(Properties.ResourceStrings.GetRequired("UE4SSDownloadUrl"), ".zip", progress, token);
            await InstallArchiveAsync(path, archive, progress, token);
        }, token, requiresProfiles: false);

    internal static async Task InstallArchiveAsync(string path, string archive, IProgress<OperationProgress>? progress = null, CancellationToken token = default)
    {
        string staging = Path.Combine(AppStorage.DownloadsPath, Guid.NewGuid().ToString("N"));
        string rollback = Path.Combine(AppStorage.DownloadsPath, Guid.NewGuid().ToString("N"));
        bool installedSuccessfully = false;
        try
        {
            progress?.Report(new("Validating UE4SS…"));
            await Task.Run(() => ModPackageInstaller.ExtractArchive(archive, staging, token), token);
            List<string> files = ValidatePackage(path, staging);
            Directory.CreateDirectory(rollback);
            InstallValidatedPackage(path, staging, rollback, files, progress, token);
            installedSuccessfully = true;
        }
        finally
        {
            ModOperations.DeleteDownload(archive);
            TryCleanup(staging);
            // Retain rollback content after failures so recovery remains possible.
            if (installedSuccessfully) TryCleanup(rollback);
        }
    }

    private static List<string> ValidatePackage(string path, string staging)
    {
        foreach (string required in new[] { RuntimeDllName, ProxyDllName, "UE4SS-settings.ini", Path.Combine("Mods", "mods.txt"),
            Path.Combine("Mods", "Keybinds", "Scripts", "main.lua"), Path.Combine("Mods", "shared", "UEHelpers", "UEHelpers.lua") })
            if (!File.Exists(Path.Combine(staging, required))) throw new InvalidDataException("The UE4SS download is incomplete. Missing: " + required);
        List<string> files = [];
        foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(staging, file);
            if (!IsPackageFile(relative)) throw new InvalidDataException("Unexpected file in the UE4SS package: " + relative);
            SafePaths.Child(path, relative);
            files.Add(relative);
        }
        return files;
    }

    private static void InstallValidatedPackage(string path, string staging, string rollback, List<string> files, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        List<(string Relative, bool Existed)> changed = [];
        try
        {
            GameRuntime.EnsureGameClosed(path);
            progress?.Report(new("Installing UE4SS; keeping existing mods and settings…"));
            List<string> obsolete = GetObsoleteRuntimeFiles(path, files);
            CopyPackageFiles(path, staging, rollback, files, changed, token);
            RemoveObsoleteRuntimeFiles(path, rollback, obsolete, changed, token);
            progress?.Report(new("Finalizing UE4SS installation…"));
            token.ThrowIfCancellationRequested();
            SaveInstallationRecord(path, files);
        }
        catch (Exception original)
        {
            List<Exception> failures = [original];
            RestoreChangedFiles(path, rollback, changed, failures);
            if (failures.Count > 1) throw new AggregateException("UE4SS installation failed and some files could not be restored. Backup: " + rollback, failures);
            throw;
        }
    }

    private static List<string> GetObsoleteRuntimeFiles(string path, List<string> packageFiles)
    {
        HashSet<string> current = new(packageFiles, StringComparer.OrdinalIgnoreCase);
        HashSet<string> obsolete = new(StringComparer.OrdinalIgnoreCase);
        foreach (var record in SettingsManager.Current.Ue4ssInstallations)
        {
            if (!ModOperations.SameGame(record.GamePath, path)) continue;
            foreach (string relative in GetRuntimeTargets(path, record))
                if (!current.Contains(relative)) obsolete.Add(relative);
        }
        return obsolete.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void RemoveObsoleteRuntimeFiles(string path, string rollback, List<string> obsolete, List<(string Relative, bool Existed)> changed, CancellationToken token)
    {
        foreach (string relative in obsolete)
        {
            token.ThrowIfCancellationRequested();
            string target = SafePaths.Child(path, relative);
            if (!File.Exists(target)) continue;
            BackupRuntimeFile(path, relative, rollback);
            changed.Add((relative, true));
            File.Delete(target);
        }
    }

    private static void CopyPackageFiles(string path, string staging, string rollback, List<string> files, List<(string Relative, bool Existed)> changed, CancellationToken token)
    {
        foreach (string relative in files)
        {
            token.ThrowIfCancellationRequested();
            string destination = SafePaths.Child(path, relative);
            // Existing settings, mods.txt, and support scripts may have user customizations.
            if (File.Exists(destination) && !IsRuntimeFile(relative)) continue;
            bool existed = File.Exists(destination);
            if (existed)
            {
                string saved = SafePaths.Child(rollback, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(destination, saved);
            }
            changed.Add((relative, existed));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(SafePaths.Child(staging, relative), destination, overwrite: true);
        }
    }

    private static void SaveInstallationRecord(string path, List<string> files)
    {
        var oldRecords = SettingsManager.Current.Ue4ssInstallations.ToList();
        SettingsManager.Current.Ue4ssInstallations.RemoveAll(r => ModOperations.SameGame(r.GamePath, path));
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord { GamePath = path, Files = files,
            Version = ReadRuntimeVersion(Path.Combine(path, RuntimeDllName)) });
        try { SettingsManager.SaveSettings(); }
        catch { SettingsManager.Current.Ue4ssInstallations = oldRecords; throw; }
    }

    private static void RestoreChangedFiles(string path, string rollback, List<(string Relative, bool Existed)> changed, List<Exception> failures)
    {
        foreach (var item in changed.AsEnumerable().Reverse())
        {
            try
            {
                string destination = SafePaths.Child(path, item.Relative);
                if (item.Existed) File.Copy(SafePaths.Child(rollback, item.Relative), destination, overwrite: true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            catch (Exception ex) { failures.Add(ex); }
        }
    }

    public static Task UninstallAsync() => ModOperations.RunAsync(UninstallRuntimeAsync, CancellationToken.None, requiresProfiles: false);

    private static async Task UninstallRuntimeAsync(string path)
    {
        var record = SettingsManager.Current.Ue4ssInstallations.Find(r => ModOperations.SameGame(r.GamePath, path));
        if (record is null) throw new InvalidOperationException("Repair UE4SS once to record its files before removing this older installation.");
        var targets = GetRuntimeTargets(path, record);
        string rollback = Path.Combine(AppStorage.DownloadsPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollback);
        var before = SettingsManager.CaptureSnapshot();
        BackupRuntimeFiles(path, targets, rollback);
        try
        {
            await Task.Run(() => DeleteRuntimeFiles(path, targets));
            SettingsManager.Current.Ue4ssInstallations.Remove(record);
            SettingsManager.SaveSettings();
        }
        catch (Exception original)
        {
            List<Exception> failures = [original];
            RestoreRuntimeFiles(path, targets, rollback, failures);
            try { SettingsManager.RestoreSnapshot(before); }
            catch (Exception ex) { failures.Add(ex); }
            if (failures.Count > 1) throw new AggregateException("UE4SS removal failed; recovery backup: " + rollback, failures);
            throw;
        }
        TryCleanup(rollback);
        AppStorage.Log("Removed UE4SS runtime files; user mods and configuration were preserved.");
    }

    private static List<string> GetRuntimeTargets(string path, Ue4SsInstallationRecord record)
    {
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in record.Files)
        {
            if (!IsRuntimeFile(relative)) continue;
            targets.Add(Path.GetRelativePath(path, SafePaths.Child(path, relative)));
        }
        return targets.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void BackupRuntimeFile(string path, string relative, string rollback)
    {
        string saved = SafePaths.Child(rollback, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.Copy(SafePaths.Child(path, relative), saved);
    }

    private static void BackupRuntimeFiles(string path, List<string> targets, string rollback)
    {
        foreach (string relative in targets)
            if (File.Exists(SafePaths.Child(path, relative))) BackupRuntimeFile(path, relative, rollback);
    }

    private static void DeleteRuntimeFiles(string path, List<string> targets)
    {
        foreach (string relative in targets)
        {
            string file = SafePaths.Child(path, relative);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static void RestoreRuntimeFiles(string path, List<string> targets, string rollback, List<Exception> failures)
    {
        foreach (string relative in targets)
        {
            try
            {
                string saved = SafePaths.Child(rollback, relative);
                if (File.Exists(saved)) File.Copy(saved, SafePaths.Child(path, relative), overwrite: true);
            }
            catch (Exception ex) { failures.Add(ex); }
        }
    }

    private static bool IsRuntimeFile(string relative)
    {
        string? directory = Path.GetDirectoryName(relative);
        return Path.GetExtension(relative).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(directory) || directory.Equals("ue4ss", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPackageFile(string relative) => IsRuntimeFile(relative) && string.IsNullOrEmpty(Path.GetDirectoryName(relative))
        || relative.Equals("UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase)
        || relative.Equals(Path.Combine("Mods", "mods.txt"), StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(Path.Combine("Mods", "Keybinds") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(Path.Combine("Mods", "shared") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void TryCleanup(string path)
    {
        try { SafePaths.DeleteScratch(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppStorage.Log("Could not remove a temporary installation folder.", ex); }
    }
}
