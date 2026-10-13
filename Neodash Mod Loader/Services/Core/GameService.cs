using System.Diagnostics;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Application-facing game access backed by the existing path and process safeguards.</summary>
public sealed class GameService : IGameService
{
    public string GetPath() => GamePathResolver.GetNeodashWin64Path();
    public string RequirePath() => GamePathResolver.RequireGamePath();
    public bool IsRunning(string path) => GameRuntime.IsGameRunning(path);
    public void EnsureClosed(string? gamePath = null) => GameRuntime.EnsureGameClosed(gamePath ?? RequirePath());
    public void Launch()
    {
        _ = RequirePath();
        Process.Start(new ProcessStartInfo(Properties.ResourceStrings.GetRequired("NeodashLaunchUri")) { UseShellExecute = true });
    }
}
