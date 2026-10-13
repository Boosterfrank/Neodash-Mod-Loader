using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public interface IAppDiagnostics
{
    Version Version { get; }

    void Log(string message, Exception? exception = null);
    void LogCrash(Exception exception);
    string GetLocation(string name);
    bool CanOpenLocation(string name);
}

public interface ISettingsService
{
    string? Theme { get; }

    bool IsSetupComplete { get; }

    string? LoadError { get; }

    void Reload();
    void SetTheme(string theme);
    void SetSetupComplete(bool complete);
    void ChooseGameFolder(string selectedFolder, bool ensureGameClosed);
    void DetectGame();
}

public interface IGameService
{
    string GetPath();
    string RequirePath();
    bool IsRunning(string path);
    void EnsureClosed(string? gamePath = null);
    void Launch();
}

public interface IModWorkspace
{
    bool IsBusy { get; }

    event EventHandler? Changed;
    IReadOnlyList<InstalledModState> GetInstalledMods();
    string GetModFolder(string name);
    string GetEntryPoint(string name, bool enabled);
    IReadOnlyList<string> GetOwnedFolders(string name);
    void ValidateEdits();
    void ApplyChanges(IReadOnlyDictionary<string, bool> changes);
    Task ImportZipAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task ImportFolderAsync(string path, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task UninstallAsync(string name);
    Task RepairAsync(string name, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task RestoreAsync(string name);
}

public interface IProfileService
{
    IReadOnlyList<ModProfile> Profiles { get; }

    ModProfile ActiveProfile { get; }

    int MaxNameLength { get; }

    string? ValidateName(string? name, ModProfile? renaming = null);
    void Create(string name);
    void Rename(ModProfile profile, string name);
    void Delete(ModProfile profile);
    void Export(string path);
    ModProfile Import(string path);
    ProfileSwitchPlan PrepareSwitch(ModProfile target);
    ProfileSwitchResult CommitSwitch(ProfileSwitchPlan plan);
}

public interface IModCatalog
{
    Task<ModLibraryResponse> LoadAsync(CancellationToken token = default);
    CatalogModState GetState(ModLibraryItem mod);
    IReadOnlyList<ModFolderLocation> GetFolders(ModLibraryItem mod);
    IReadOnlyList<string> GetUninstallFolders(ModLibraryItem mod);
    Task InstallAsync(ModLibraryItem mod, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task UninstallAsync(ModLibraryItem mod);
}

public interface IRuntimeService
{
    bool IsInstalled(string gamePath);
    bool CanRemove(string gamePath);
    string GetStatus(string gamePath);
    bool HasConfiguration { get; }

    ConfigurationLocation GetConfiguration();
    Task InstallAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task UninstallAsync();
}

public interface IConfigurationService
{
    ConfigurationLocation GetModConfiguration(string name);
    string[] FindFiles(string gameRoot, string folderRoot);
}

public interface IApplicationUpdates
{
    Task<Version?> FindUpdateAsync();
    Task<string> DownloadAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default);
}
