using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>UI-free installed-mod workflow adapter; ModOperations retains package ownership and rollback rules.</summary>
public sealed class ModWorkspace(IGameService game, IAppDiagnostics diagnostics) : IModWorkspace
{
    public bool IsBusy => ModOperations.IsBusy;

    public event EventHandler? Changed { add => ModOperations.Changed += value; remove => ModOperations.Changed -= value; }

    public IReadOnlyList<InstalledModState> GetInstalledMods()
    {
        string path = game.RequirePath();
        if (!game.IsRunning(path))
            _ = SettingsMigration.MigrateLegacyModState(path);
        return ModManager.GetMods(path).OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).Select(mod => CreateState(path, mod.Name, mod.IsEnabled)).ToList();
    }

    public void ValidateEdits()
    {
        ProfileManager.EnsureWritable();
        string path = game.RequirePath();
        ProfileManager.EnsureProfileGamePath(ProfileManager.ActiveProfile, path);
    }

    public string GetModFolder(string name)
    {
        string path = game.RequirePath();
        var mod = ModManager.GetMods(path).FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(mod.Name))
            throw new DirectoryNotFoundException("The mod folder no longer exists.");
        return SafePaths.Child(path, Path.GetRelativePath(path, ModManager.GetModPath(path, mod.Name, mod.IsEnabled)));
    }

    public string GetEntryPoint(string name, bool enabled)
    {
        string path = game.RequirePath();
        return ReadEntryPoint(path, ModManager.GetModPath(path, name, enabled));
    }

    public string? GetLuaScriptFolder(string name) => ModEntryPoints.GetLuaDirectory(GetModFolder(name));
    public int MaxLuaModNameLength => ModOperations.MaximumLuaModNameLength;
    public string? ValidateLuaModName(string? name) => ModOperations.ValidateLuaModName(name);
    public Task CreateLuaModAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => ModOperations.CreateLuaModAsync(name, progress, token);

    public IReadOnlyList<string> GetOwnedFolders(string name) => ModOperations.FindRecordForFolder(name)?.FolderNames ?? [name];
    public void ApplyChanges(IReadOnlyDictionary<string, bool> changes)
    {
        if (changes.Count == 0)
            return;
        string path = game.RequirePath();
        ModStateTransactions.ApplyChanges(path, changes.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
    }

    public Task ImportZipAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => ModOperations.ImportZipAsync(path, progress, token);
    public Task ImportFolderAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => ModOperations.ImportFolderAsync(path, progress, token);
    public Task UninstallAsync(string name) => ModOperations.UninstallFolderAsync(name);
    public Task RepairAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => ModOperations.ReinstallFolderAsync(name, progress, token);
    public Task RestoreAsync(string name) => ModOperations.RestoreFolderAsync(name);
    private InstalledModState CreateState(string gamePath, string name, bool enabled)
    {
        var record = ModOperations.FindRecordForFolder(name);
        return new InstalledModState(name, enabled, ReadEntryPoint(gamePath, ModManager.GetModPath(gamePath, name, enabled)), record is { DownloadUrl: { Length: > 0 } }, record?.BackupPath is string backup && Directory.Exists(backup), record is null ? string.Empty : "Tracked: " + record.Version);
    }

    private string ReadEntryPoint(string gamePath, string modPath)
    {
        try
        {
            return ModEntryPoints.GetDisplayPath(gamePath, modPath);
        }
        catch (Exception ex)when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Log("Could not read a mod's entry point.", ex);
            return string.Empty;
        }
    }
}
