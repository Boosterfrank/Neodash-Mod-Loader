using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IUserDialogs
{
    bool IsDialogOpen { get; }

    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string primary = "Continue");
    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message);
    Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true);
}
