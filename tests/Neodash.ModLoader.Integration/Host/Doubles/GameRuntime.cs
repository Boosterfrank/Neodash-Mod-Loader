namespace Neodash_Mod_Loader;
public static class GameRuntime
{
    public static Func<string, bool>? RunningProbe { get; set; }

    public static bool IsGameRunning(string win64Path) => RunningProbe?.Invoke(win64Path) ?? false;
    public static void EnsureGameClosed(string win64Path)
    {
        if (IsGameRunning(win64Path))
            throw new InvalidOperationException("Close Neodash before installing, updating, removing, or changing mods.");
    }
}
