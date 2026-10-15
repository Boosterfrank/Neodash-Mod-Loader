using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IModWorkspace
{
    bool IsBusy { get; }

    event EventHandler? Changed;
    IReadOnlyList<InstalledModState> GetInstalledMods();
    string GetModFolder(string name);
    string? GetLuaScriptFolder(string name);
    int MaxLuaModNameLength { get; }

    string? ValidateLuaModName(string? name);
    string GetEntryPoint(string name, bool enabled);
    IReadOnlyList<string> GetOwnedFolders(string name);
    void ValidateEdits();
    void ApplyChanges(IReadOnlyDictionary<string, bool> changes);
    Task ImportZipAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task ImportFolderAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task CreateLuaModAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task UninstallAsync(string name);
    Task RepairAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task RestoreAsync(string name);
}
