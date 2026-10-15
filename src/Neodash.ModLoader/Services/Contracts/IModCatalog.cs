using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IModCatalog
{
    Task<ModLibraryResponse> LoadAsync(CancellationToken token = default);
    CatalogModState GetState(ModLibraryItem mod);
    IReadOnlyList<ModFolderLocation> GetFolders(ModLibraryItem mod);
    IReadOnlyList<string> GetUninstallFolders(ModLibraryItem mod);
    Task InstallAsync(ModLibraryItem mod, IProgress<OperationProgress>? progress = null, CancellationToken token = default);
    Task UninstallAsync(ModLibraryItem mod);
}
