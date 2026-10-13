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
        "ue4ss" => Ue4SsService.GetLogPath(GamePathResolver.RequireGamePath()),
        "crash" => Path.Combine(AppStorage.DirectoryPath, "crash.txt"),
        "config" => Path.Combine(GamePathResolver.RequireGamePath(), "UE4SS-settings.ini"),
        "settings" => AppStorage.DirectoryPath,
        "backups" => CreateBackupsDirectory(),
        "folder" => GamePathResolver.RequireGamePath(),
        "operations" => AppStorage.LogPath,
        _ => AppStorage.LogPath
    };

    public bool CanOpenLocation(string name)
    {
        if (name == "ue4ss")
        {
            string path = GamePathResolver.GetNeodashWin64Path();
            return path.Length > 0 && File.Exists(Ue4SsService.GetLogPath(path));
        }
        return name switch
        {
            "operations" => File.Exists(AppStorage.LogPath),
            "crash" => File.Exists(Path.Combine(AppStorage.DirectoryPath, "crash.txt")),
            "settings" => Directory.Exists(AppStorage.DirectoryPath),
            "backups" => Directory.Exists(AppStorage.BackupsPath),
            _ => false
        };
    }
    private static string CreateBackupsDirectory()
    {
        Directory.CreateDirectory(AppStorage.BackupsPath);
        return AppStorage.BackupsPath;
    }
}
