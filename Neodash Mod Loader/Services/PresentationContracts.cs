using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}

public interface IDesktopFiles
{
    bool CanOpenInCode { get; }
    void OpenInCode(string folder);
    Task<string?> PickFileAsync(params string[] extensions);
    Task<string?> PickFolderAsync();
    Task<string?> SaveFileAsync(string suggestedName, string extension);
    void OpenPath(string path);
}

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

public interface IUserDialogs
{
    bool IsDialogOpen { get; }

    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string primary = "Continue");
    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message);
    Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true);
}

public interface IModEditSession
{
    IReadOnlyList<EditableMod> Mods { get; }

    bool HasUnsavedChanges { get; }

    event EventHandler? Changed;
    void Reload();
    void Discard();
    Task<bool> SaveAsync();
}

public interface INavigationGuard
{
    bool IsBlocked { get; }

    bool HasUnsavedChanges { get; }

    Task<bool> ResolveUnsavedChangesAsync(string message);
    Task<bool> EnsureNotBusyAsync();
}
