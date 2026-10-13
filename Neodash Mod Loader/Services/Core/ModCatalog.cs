using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Catalog presentation state without UI dependencies; package operations remain in ModOperations.</summary>
public sealed class ModCatalog(IGameService game, IModWorkspace workspace) : IModCatalog
{
    public Task<ModLibraryResponse> LoadAsync(CancellationToken token = default) => ModOperations.LoadCatalogAsync(token);
    public CatalogModState GetState(ModLibraryItem mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        var record = ModOperations.FindRecord(mod);
        List<string> folders = ModOperations.GetInstalledFolders(mod);
        string? installedVersion = folders.Count > 0 ? record?.Version ?? SettingsManager.Current.GetInstalledModVersion(mod.Name) : null;
        bool installed = folders.Count > 0;
        bool incomplete = record is not null && folders.Count != record.FolderNames.Count;
        bool update = installed && ModVersion.Compare(mod.Version, installedVersion) > 0;
        string status = installed ? "Installed: " + (installedVersion ?? "version unknown") : "Not installed";
        string button = installed ? "Installed" : "Install";
        if (update)
            button = "Update";
        if (incomplete)
        {
            status = "Missing files — repair recommended";
            button = "Repair";
        }

        bool hasGame = game.GetPath().Length > 0;
        return new CatalogModState(installed, update, installed && hasGame, installed && !incomplete && !update, hasGame && (!installed || incomplete || update), installedVersion, status, button);
    }

    public IReadOnlyList<ModFolderLocation> GetFolders(ModLibraryItem mod)
    {
        return ModOperations.GetInstalledFolders(mod)
            .Select(name => new ModFolderLocation(name, workspace.GetModFolder(name)))
            .ToList();
    }

    public IReadOnlyList<string> GetUninstallFolders(ModLibraryItem mod) => ModOperations.FindRecord(mod)?.FolderNames ?? ModOperations.GetInstalledFolders(mod);
    public Task InstallAsync(ModLibraryItem mod, IProgress<OperationProgress>? progress = null, CancellationToken token = default) => ModOperations.InstallCatalogAsync(mod, progress, token);
    public Task UninstallAsync(ModLibraryItem mod) => ModOperations.UninstallCatalogAsync(mod);
}
