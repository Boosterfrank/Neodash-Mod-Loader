namespace Neodash_Mod_Loader
{

// These test-only substitutes keep real services inside tests/.scratch and make the game/runtime boundary deterministic.
public static class AppStorage
{
    public static string DirectoryPath => Environment.GetEnvironmentVariable("NEODASH_TEST_STORAGE")
        ?? throw new InvalidOperationException("Test storage was not configured.");
    public static string BackupsPath => Path.Combine(DirectoryPath, "Backups");
    public static string DownloadsPath => Path.Combine(DirectoryPath, "Downloads");
    public static string LogPath => Path.Combine(DirectoryPath, "operations.log");
    public static int SettingsWriteCount { get; private set; }
    public static int? FailSettingsWriteNumber { get; set; }
    public static string? FailWritePath { get; set; }

    public static void WriteAtomically(string path, string content)
    {
        WriteAtomically(path, System.Text.Encoding.UTF8.GetBytes(content));
    }

    public static void WriteAtomically(string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Path.GetFileName(path).Equals("settings.json", StringComparison.OrdinalIgnoreCase))
        {
            SettingsWriteCount++;
            if (SettingsWriteCount == FailSettingsWriteNumber) throw new IOException("Injected settings write failure.");
        }
        if (FailWritePath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(FailWritePath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Injected file write failure.");
        AtomicFile.Write(path, content);
    }

    public static void Log(string message, Exception? exception = null)
    {
        // Logging is intentionally ignored so these tests exercise only their explicit filesystem fixtures.
    }
}

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

}

namespace Neodash_Mod_Loader.Properties
{

internal static class ResourceStrings
{
    public static string GetRequired(string name) => "https://example.invalid/" + name;
}
}
