using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
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
