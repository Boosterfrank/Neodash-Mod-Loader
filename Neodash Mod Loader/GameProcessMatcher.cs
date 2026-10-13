namespace Neodash_Mod_Loader;

/// <summary>Matches a queried process executable to the currently selected game installation.</summary>
internal static class GameProcessMatcher
{
    internal static bool MatchesSelectedGame(string win64Path, string? executablePath)
    {
        // A matching process without a readable executable path must be treated as the selected game.
        if (string.IsNullOrWhiteSpace(executablePath)) return true;
        if (string.IsNullOrWhiteSpace(win64Path)) return false;

        string actual;
        try { actual = Path.GetFullPath(executablePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return true; }

        foreach (string executableName in new[] { "Runway-Win64-Shipping.exe", "Runway.exe", "Neodash.exe" })
        {
            string expected = Path.GetFullPath(Path.Combine(win64Path, executableName));
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
