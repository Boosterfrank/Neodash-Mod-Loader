using System.Text;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

public static partial class ModOperations
{
    public const int MaximumLuaModNameLength = 64;

    public static string? ValidateLuaModName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a mod name.";
        if (name.Length > MaximumLuaModNameLength) return $"Use {MaximumLuaModNameLength} characters or fewer.";
        if (name != name.Trim() || name.StartsWith(';')) return "Don't start the name with a semicolon or use leading or trailing whitespace.";
        if (!ModPackageInstaller.IsValidFolderName(name)) return "Use a valid folder name without reserved names, path separators, or a trailing space or dot.";
        string path = GamePathResolver.GetNeodashWin64Path();
        return path.Length == 0 ? "Choose the Neodash game folder in Settings first." : FindNewModConflict(path, name);
    }

    public static Task CreateLuaModAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) =>
        RunAsync(path => CreateLuaModCoreAsync(path, name, progress, token), token);

    private static async Task CreateLuaModCoreAsync(string path, string name, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        string? error = ValidateLuaModName(name);
        if (error is not null) throw new ArgumentException(error, nameof(name));
        string staging = SafePaths.Child(AppStorage.DownloadsPath, "lua-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = SafePaths.Child(staging, name);
            string scripts = SafePaths.Child(source, "Scripts");
            Directory.CreateDirectory(scripts);
            // The name is a Lua string literal, never executable source.
            string literal = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string script = $"-- {name}\n-- UE4SS Lua mod. Add your code below.\n\nprint(\"[{literal}] Mod loaded\\n\")\n";
            await File.WriteAllTextAsync(SafePaths.Child(scripts, "main.lua"), script, new UTF8Encoding(false), token);
            progress?.Report(new("Creating " + name));
            var result = await Task.Run(() => ModPackageInstaller.InstallFolder(path, source, AppStorage.BackupsPath, [],
                (_, _) => FindNewModConflict(path, name), token, revalidateBeforeMutation: true), token);
            EnsureSuccess(result, token);
            await SaveInstallationAsync(path, result, new InstalledModRecord
            {
                Id = CanonicalLocalId(result.Folders), Name = name, Version = "local", GamePath = path,
                FolderNames = result.Folders
            });
            // A fresh mod has no previous version to restore. Keep the snapshot only until commit succeeds.
            if (result.BackupPath is not null) PruneBackup(result.BackupPath, null);
        }
        finally
        {
            try { SafePaths.DeleteScratch(staging); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppStorage.Log("Could not clean the Lua mod staging folder.", ex); }
        }
    }

    private static string? FindNewModConflict(string path, string name)
    {
        foreach (string parent in new[] { "Mods", "DisabledMods" })
        {
            string root = SafePaths.Child(path, parent);
            if (!Directory.Exists(root)) continue;
            foreach (string entry in Directory.EnumerateFileSystemEntries(root))
                if (Path.GetFileName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)) return "A mod or file with this name already exists. Choose a different name.";
        }
        return FindRecordForFolder(name) is null ? null : "This name belongs to an installed package. Choose a different name.";
    }
}
