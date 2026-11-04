namespace Neodash_Mod_Loader.Services;

public sealed class AppUpdateCoordinator(IApplicationUpdates updates, IModWorkspace workspace, IModEditSession edits,
    IUserDialogs dialogs, IInstallerLauncher launcher, IAppDiagnostics diagnostics) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _checking;
    private bool _disposed;
    private bool IsBlocked => _disposed || workspace.IsBusy || dialogs.IsDialogOpen;

    public async Task CheckAsync(bool showUpToDate = false)
    {
        if (_checking || IsBlocked) return;
        _checking = true;
        bool userAccepted = false;
        CancellationToken lifetime = _lifetime.Token;
        try
        {
            var result = await FindUpdateAsync(showUpToDate, lifetime);
            // Background checks must not interrupt work started while the network request was pending.
            if (IsBlocked || !result.Completed) return;
            if (result.Version is null)
            {
                if (showUpToDate)
                    await dialogs.ShowMessageAsync("No update available", "You are using the latest available version.");
                return;
            }
            userAccepted = await dialogs.ConfirmAsync("Update available", $"Version {result.Version} is available. Download and open its installer?", "Download update");
            if (!userAccepted || IsBlocked) return;
            if (edits.HasUnsavedChanges && !await edits.SaveAsync()) return;
            if (IsBlocked) return;
            string? installer = null;
            bool success = await dialogs.RunAsync("Download application update", async (progress, token) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
                installer = await updates.DownloadAsync(progress, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                progress.Report(new("Download complete — preparing installer…", 100));
            });
            if (!_disposed && success && installer is not null)
            {
                if (IsBlocked)
                    await dialogs.ShowMessageAsync("Update ready to install",
                        "The installer was downloaded, but the app is busy. Finish the current task, then open this file to install the update:\n\n" + installer);
                else
                    launcher.OpenInstaller(installer);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { /* Window closed. */ }
        catch (Exception ex)
        {
            diagnostics.Log("Application update check failed.", ex);
            if (!_disposed && (showUpToDate || userAccepted))
                await dialogs.ShowMessageAsync("Application update failed", ex.Message);
        }
        finally { _checking = false; }
    }

    private async Task<(bool Completed, Version? Version)> FindUpdateAsync(bool showProgress, CancellationToken lifetime)
    {
        if (!showProgress) return (true, await updates.FindUpdateAsync(lifetime));
        Version? version = null;
        bool success = await dialogs.RunAsync("Check for application updates", async (progress, token) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            progress.Report(new("Checking for updates…"));
            version = await updates.FindUpdateAsync(linked.Token);
        });
        return (success, version);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
