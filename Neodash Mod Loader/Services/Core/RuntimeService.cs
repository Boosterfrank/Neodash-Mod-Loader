using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Application boundary for the tracked UE4SS runtime installer.</summary>
public sealed class RuntimeService(IGameService game) : IRuntimeService
{
    public bool IsInstalled(string gamePath) => File.Exists(Path.Combine(gamePath, "UE4SS.dll")) || File.Exists(Path.Combine(gamePath, "ue4ss", "UE4SS.dll"));
    public string GetStatus(string gamePath) => Ue4SsService.GetStatus(gamePath);
    public bool HasConfiguration => game.GetPath()is { Length: > 0 } path && File.Exists(Path.Combine(path, "UE4SS-settings.ini"));

    public ConfigurationLocation GetConfiguration()
    {
        string path = game.RequirePath();
        string config = SafePaths.Child(path, "UE4SS-settings.ini");
        return new ConfigurationLocation(path, path, [config]);
    }

    public Task InstallAsync(IProgress<OperationProgress>? progress = null, CancellationToken token = default) => Ue4SsService.InstallAsync(progress, token);
    public Task UninstallAsync() => Ue4SsService.UninstallAsync();
}
