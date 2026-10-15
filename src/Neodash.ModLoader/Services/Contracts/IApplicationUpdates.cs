using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IApplicationUpdates
{
    Task<Version?> FindUpdateAsync(CancellationToken token = default);
    Task<string> DownloadAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default);
}
