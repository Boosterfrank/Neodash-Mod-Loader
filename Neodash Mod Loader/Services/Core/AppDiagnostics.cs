using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Adapter boundary between application workflows and the established storage diagnostics.</summary>
public sealed class AppDiagnostics : IAppDiagnostics
{
    public Version Version => typeof(AppDiagnostics).Assembly.GetName().Version ?? new Version(0, 0);

    public void Log(string message, Exception? exception = null) => AppStorage.Log(message, exception);
    public void LogCrash(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppStorage.DirectoryPath);
            File.AppendAllText(Path.Combine(AppStorage.DirectoryPath, "crash.txt"), $"[{DateTime.Now}] {exception.Message}\n{exception.StackTrace}\n\n");
        }
        catch
        {
        // Crash logging is best effort: a logging failure must not replace the original unhandled exception.
        }
    }

    public string GetLocation(string name) => name switch
    {
        "ue4ss" => Path.Combine(GamePathResolver.RequireGamePath(), "UE4SS.log"),
        "crash" => Path.Combine(AppStorage.DirectoryPath, "crash.txt"),
        "config" => Path.Combine(GamePathResolver.RequireGamePath(), "UE4SS-settings.ini"),
        "settings" => AppStorage.DirectoryPath,
        "backups" => CreateBackupsDirectory(),
        "folder" => GamePathResolver.RequireGamePath(),
        "operations" => AppStorage.LogPath,
        _ => AppStorage.LogPath
    };
    private static string CreateBackupsDirectory()
    {
        Directory.CreateDirectory(AppStorage.BackupsPath);
        return AppStorage.BackupsPath;
    }
}
