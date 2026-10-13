namespace Neodash_Mod_Loader.Services;

public sealed partial class AppUpdateCoordinator(IApplicationUpdates updates, IModWorkspace workspace, IModEditSession edits, IUserDialogs dialogs, IWindowContext window, IAppDiagnostics diagnostics) : IDisposable
{
    private bool _checking;
    private bool _disposed;
    public async Task CheckAsync(bool showUpToDate = false)
    {
        if (_disposed || _checking || workspace.IsBusy)
            return;
        _checking = true;
        try
        {
            Version? version = await updates.FindUpdateAsync();
            if (_disposed)
                return;
            if (version is null)
            {
                if (showUpToDate)
                    await dialogs.ShowMessageAsync("No update available", "You are using the latest available version.");
                return;
            }

            if (!await dialogs.ConfirmAsync("Update available", $"Version {version} is available. Download and open its installer?", "Download update"))
                return;
            if (edits.HasUnsavedChanges && !await edits.SaveAsync())
                return;
            string? installer = null;
            bool success = await dialogs.RunAsync("Download application update", async (progress, token) => installer = await updates.DownloadAsync(progress, token));
            if (!_disposed && success && installer is not null)
                window.OpenInstaller(installer);
        }
        catch (Exception ex)
        {
            diagnostics.Log("Application update check failed.", ex);
            if (!_disposed && showUpToDate)
                await dialogs.ShowMessageAsync("Update check failed", ex.Message);
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose() => _disposed = true;
}
