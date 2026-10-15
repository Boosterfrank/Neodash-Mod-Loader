using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IGameService
{
    string GetPath();
    string RequirePath();
    bool IsRunning(string path);
    void EnsureClosed(string? gamePath = null);
    void Launch();
}
