using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IDesktopFiles
{
    bool CanOpenInCode { get; }

    void OpenInCode(string folder);
    Task<string?> PickFileAsync(params string[] extensions);
    Task<string?> PickFolderAsync();
    Task<string?> SaveFileAsync(string suggestedName, string extension);
    void OpenPath(string path);
}
