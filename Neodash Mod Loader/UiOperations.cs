using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader;

public sealed class UiOperations(IWindowContext window, IAppDiagnostics diagnostics) : IDesktopDialogs
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    public bool IsDialogOpen => _dialogGate.CurrentCount == 0;

    public async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = window.XamlRoot;
        await _dialogGate.WaitAsync();
        try { return await dialog.ShowAsync(); }
        finally { _dialogGate.Release(); }
    }

    public async Task ShowMessageAsync(string title, string message) =>
        _ = await ShowDialogAsync(new ContentDialog { Title = title, Content = message, CloseButtonText = "OK" });

    public async Task<bool> ConfirmAsync(string title, string message, string primary = "Continue")
    {
        return await ShowDialogAsync(new ContentDialog { Title = title, Content = message,
            PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close })
            == ContentDialogResult.Primary;
    }

    public async Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message) =>
        await ShowDialogAsync(new ContentDialog { Title = "Unsaved Changes", Content = message,
            PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel" }) switch
        {
            ContentDialogResult.Primary => UnsavedChangesChoice.Save,
            ContentDialogResult.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel
        };

    public async Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true)
    {
        using var cancellation = new CancellationTokenSource();
        var text = new TextBlock { Text = "Starting…", TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        var bar = new ProgressBar { IsIndeterminate = true, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(bar, "Operation progress");
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(text);
        content.Children.Add(bar);
        var dialog = new ContentDialog { XamlRoot = window.XamlRoot, Title = title, Content = content, CloseButtonText = allowCancellation ? "Cancel" : string.Empty };
        bool completed = false;
        dialog.Closing += (_, args) => { if (!completed) { args.Cancel = true; if (allowCancellation) { cancellation.Cancel(); text.Text = "Cancelling safely…"; } } };
        await _dialogGate.WaitAsync();
        Windows.Foundation.IAsyncOperation<ContentDialogResult> show;
        try { show = dialog.ShowAsync(); }
        catch { _dialogGate.Release(); throw; }
        var progress = new Progress<OperationProgress>(value =>
        {
            text.Text = value.Message;
            bar.IsIndeterminate = value.Percent is null;
            if (value.Percent is double percent) bar.Value = Math.Clamp(percent, 0, 100);
        });
        Exception? failure = null;
        bool cancelled = false;
        try { await action(progress, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
        catch (Exception ex) { failure = ex; diagnostics.Log(title + " failed.", ex); }
        finally { completed = true; dialog.Hide(); try { await show; } finally { _dialogGate.Release(); } }
        if (failure is not null)
            await ShowMessageAsync(title + " failed", failure.Message);
        return failure is null && !cancelled;
    }
}
