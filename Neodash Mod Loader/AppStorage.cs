using System.Text;

namespace Neodash_Mod_Loader;

public static class AppStorage
{
    private static readonly object LogLock = new();
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neodash Mod Loader");
    public static string BackupsPath => Path.Combine(DirectoryPath, "Backups");
    public static string DownloadsPath => Path.Combine(DirectoryPath, "Downloads");
    public static string LogPath => Path.Combine(DirectoryPath, "operations.log");

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
            lock (LogLock)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{(exception is null ? "" : "\n" + exception)}\n");
            }
        }
        catch (IOException) { /* Logging must not hide the operation's original result. */ }
        catch (UnauthorizedAccessException) { /* Logging must not hide the operation's original result. */ }
    }
}
