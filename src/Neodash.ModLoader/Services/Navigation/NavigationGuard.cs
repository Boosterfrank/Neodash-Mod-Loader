namespace Neodash_Mod_Loader.Services;

public sealed class NavigationGuard(IModEditSession edits, IModWorkspace workspace, IUserDialogs dialogs) : INavigationGuard
{
    public bool IsBlocked => workspace.IsBusy || dialogs.IsDialogOpen;
    public bool HasUnsavedChanges => edits.HasUnsavedChanges;

    public async Task<bool> EnsureNotBusyAsync()
    {
        if (!workspace.IsBusy) return true;
        await dialogs.ShowMessageAsync(ModOperationMessages.BusyTitle, ModOperationMessages.BusyMessage);
        return false;
    }

    public async Task<bool> ResolveUnsavedChangesAsync(string message)
    {
        if (dialogs.IsDialogOpen || !await EnsureNotBusyAsync())
            return false;
        if (!edits.HasUnsavedChanges)
            return true;

        UnsavedChangesChoice choice = await dialogs.AskUnsavedChangesAsync(message);
        // The workspace or dialog state may have changed while awaiting the user's choice.
        if (IsBlocked)
            return false;
        return choice switch
        {
            UnsavedChangesChoice.Save => await edits.SaveAsync(),
            UnsavedChangesChoice.Discard => DiscardAndContinue(),
            _ => false
        };
    }

    private bool DiscardAndContinue()
    {
        edits.Discard();
        return true;
    }
}
