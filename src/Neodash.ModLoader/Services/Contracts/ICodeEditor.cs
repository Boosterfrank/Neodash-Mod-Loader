using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface ICodeEditor
{
    bool IsAvailable { get; }

    void OpenFolder(string path);
}
