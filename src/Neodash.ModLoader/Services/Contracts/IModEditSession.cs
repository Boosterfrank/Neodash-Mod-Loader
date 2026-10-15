using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IModEditSession
{
    IReadOnlyList<EditableMod> Mods { get; }

    bool HasUnsavedChanges { get; }

    event EventHandler? Changed;
    void Reload();
    void Discard();
    Task<bool> SaveAsync();
}
