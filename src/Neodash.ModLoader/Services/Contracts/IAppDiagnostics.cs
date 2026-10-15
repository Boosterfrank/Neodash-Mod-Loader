using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IAppDiagnostics
{
    Version Version { get; }

    string? CommitHash { get; }

    void Log(string message, Exception? exception = null);
    void LogCrash(Exception exception);
    string GetLocation(string name);
    bool CanOpenLocation(string name);
}
