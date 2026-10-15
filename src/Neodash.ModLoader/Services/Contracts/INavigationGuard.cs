using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface INavigationGuard
{
    bool IsBlocked { get; }

    bool HasUnsavedChanges { get; }

    Task<bool> ResolveUnsavedChangesAsync(string message);
    Task<bool> EnsureNotBusyAsync();
}
