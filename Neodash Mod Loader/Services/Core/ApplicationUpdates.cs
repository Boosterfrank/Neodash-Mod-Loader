using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Network-only update workflow; confirmation, launch, and exit remain presentation decisions.</summary>
public sealed class ApplicationUpdates(IAppDiagnostics diagnostics) : IApplicationUpdates
{
    public async Task<Version?> FindUpdateAsync()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        string text = await client.GetStringAsync(Properties.ResourceStrings.GetRequired("ModLoaderVersionUrl"));
        if (!Version.TryParse(text.Trim(), out Version? onlineVersion))
            throw new IOException("The update server returned an invalid version.");
        return onlineVersion > diagnostics.Version ? onlineVersion : null;
    }

    public Task<string> DownloadAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default) => ModOperations.DownloadAsync(Properties.ResourceStrings.GetRequired("ModLoaderDownloadUrl"), ".exe", progress, token);
}
