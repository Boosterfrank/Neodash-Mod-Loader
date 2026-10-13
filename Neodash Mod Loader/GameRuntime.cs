using System.Diagnostics;

namespace Neodash_Mod_Loader;

public static class GameRuntime
{
    public static bool IsGameRunning(string win64Path)
    {
        foreach (string name in new[] { "Runway-Win64-Shipping", "Runway", "Neodash" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (Process process in processes)
                {
                    // A matching process whose executable path cannot be inspected could be the selected game.
                    // Block in that case, but permit a process from another installation.
                    try
                    {
                        if (GameProcessMatcher.MatchesSelectedGame(win64Path, process.MainModule?.FileName)) return true;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        return true;
                    }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }


    public static void EnsureGameClosed(string win64Path)
    {
        if (IsGameRunning(win64Path)) throw new InvalidOperationException("Close Neodash before installing, updating, removing, or changing mods.");
    }
}
