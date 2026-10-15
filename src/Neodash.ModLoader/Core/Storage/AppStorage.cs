using System.Text;

namespace Neodash_Mod_Loader;

public static class AppStorage
{
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neodash Mod Loader");
    private static readonly ApplicationLogger Logger = new(DirectoryPath);
    public static string BackupsPath => Path.Combine(DirectoryPath, "Backups");
    public static string DownloadsPath => Path.Combine(DirectoryPath, "Downloads");
    public static string LogPath => ApplicationLogger.GetLatestPath(DirectoryPath);
    public static string CrashLogPath => ApplicationLogger.GetLatestPath(DirectoryPath, crash: true);

    static AppStorage()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Logger.Dispose();
    }

    public static void WriteAtomically(string path, string content)
    {
        WriteAtomically(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
    }

    public static void WriteAtomically(string path, byte[] content)
    {
        AtomicFile.Write(path, content);
    }

    public static void Log(string message, Exception? exception = null)
    {
        try
        {
            Logger.Write(message, exception);
        }
        catch (IOException) { /* Logging must not hide the operation's original result. */ }
        catch (UnauthorizedAccessException) { /* Logging must not hide the operation's original result. */ }
    }

    public static void LogCrash(Exception exception)
    {
        try { Logger.WriteCrash(exception); }
        catch (IOException) { /* Logging must not replace an unhandled exception. */ }
        catch (UnauthorizedAccessException) { /* Logging must not replace an unhandled exception. */ }
    }
}
