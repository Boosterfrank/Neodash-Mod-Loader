using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

public static class UiOperations
{
    private static readonly SemaphoreSlim DialogGate = new(1, 1);
    public static bool IsDialogOpen => DialogGate.CurrentCount == 0;

    public static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        await DialogGate.WaitAsync();
        try { return await dialog.ShowAsync(); }
        finally { DialogGate.Release(); }
    }

    public static Task<ContentDialogResult> ShowMessageAsync(XamlRoot root, string title, string message) =>
        ShowDialogAsync(new ContentDialog { XamlRoot = root, Title = title, Content = message, CloseButtonText = "OK" });

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string primary = "Continue")
    {
        return await ShowDialogAsync(new ContentDialog { XamlRoot = root, Title = title, Content = message,
            PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close })
            == ContentDialogResult.Primary;
    }

    public static async Task<bool> RunAsync(XamlRoot root, string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true)
    {
        using var cancellation = new CancellationTokenSource();
        var text = new TextBlock { Text = "Starting…", TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        var bar = new ProgressBar { IsIndeterminate = true, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(bar, "Operation progress");
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(text);
        content.Children.Add(bar);
        var dialog = new ContentDialog { XamlRoot = root, Title = title, Content = content, CloseButtonText = allowCancellation ? "Cancel" : string.Empty };
        bool completed = false;
        dialog.Closing += (_, args) => { if (!completed) { args.Cancel = true; if (allowCancellation) { cancellation.Cancel(); text.Text = "Cancelling safely…"; } } };
        await DialogGate.WaitAsync();
        Windows.Foundation.IAsyncOperation<ContentDialogResult> show;
        try { show = dialog.ShowAsync(); }
        catch { DialogGate.Release(); throw; }
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
        catch (Exception ex) { failure = ex; AppStorage.Log(title + " failed.", ex); }
        finally { completed = true; dialog.Hide(); try { await show; } finally { DialogGate.Release(); } }
        if (failure is not null)
            await ShowMessageAsync(root, title + " failed", failure.Message);
        return failure is null && !cancelled;
    }
}
