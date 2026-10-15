using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;
using System.Diagnostics;

namespace Neodash_Mod_Loader;

public sealed class UiOperations(IWindowContext window, IUiDispatcher dispatcher, IAppDiagnostics diagnostics, IUserNotifications notifications) : IDesktopDialogs
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    public bool IsDialogOpen => _dialogGate.CurrentCount == 0;

    public async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, DesktopDialogSize size = DesktopDialogSize.Standard)
    {
        await _dialogGate.WaitAsync();
        try
        {
            PrepareDialog(dialog, size);
            return await dialog.ShowAsync();
        }
        finally { _dialogGate.Release(); }
    }

    private void PrepareDialog(ContentDialog dialog, DesktopDialogSize size)
    {
        var root = window.XamlRoot;
        dialog.XamlRoot = root;
        var content = dialog.Content is string message
            ? new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            : dialog.Content as FrameworkElement;
        if (content is null) return;

        void Resize()
        {
            double width = Math.Min(size == DesktopDialogSize.Wide ? 960 : 640, Math.Max(0, root.Size.Width - 48));
            dialog.Resources["ContentDialogMaxWidth"] = width;
            dialog.Resources["ContentDialogMinWidth"] = width;
            dialog.MinWidth = width;
            dialog.MaxWidth = width;
            content.Width = Math.Max(0, width - 48);
            if (dialog.Content is ScrollViewer scroller)
                scroller.MaxHeight = Math.Max(80, root.Size.Height - 200);
        }
        dialog.Content = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
        dialog.Opened += (_, _) => root.Changed += RootChanged;
        dialog.Closed += (_, _) => root.Changed -= RootChanged;
        Resize();
    }

    public async Task ShowMessageAsync(string title, string message) =>
        _ = await ShowDialogAsync(new ContentDialog { Title = title, Content = message, CloseButtonText = "OK" });

    public async Task<bool> ConfirmAsync(string title, string message, string primary = "Continue") =>
        await ShowDialogAsync(new ContentDialog { Title = title, Content = message,
            PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary;

    public async Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string message) =>
        await ShowDialogAsync(new ContentDialog { Title = "Unsaved changes", Content = message,
            PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel" }) switch
        {
            ContentDialogResult.Primary => UnsavedChangesChoice.Save,
            ContentDialogResult.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel
        };

    public async Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool allowCancellation = true)
    {
        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { Text = "Starting…", TextWrapping = TextWrapping.Wrap, FontSize = 16 };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var remaining = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var elapsed = new TextBlock();
        var bar = new ProgressBar { IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(bar, "Operation progress");
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(status);
        content.Children.Add(bar);
        content.Children.Add(detail);
        content.Children.Add(remaining);
        content.Children.Add(elapsed);
        var dialog = new ContentDialog { Title = title, Content = content, CloseButtonText = allowCancellation ? "Cancel" : string.Empty };
        bool completed = false;
        using var progress = new DispatchedProgress<OperationProgress>(dispatcher, value =>
        {
            var display = OperationProgressPresentation.From(value);
            status.Text = display.Status;
            detail.Text = display.Detail;
            remaining.Text = display.Remaining;
            if (display.Percent is double percent)
                remaining.Text = $"{percent:N0}%" + (display.Remaining.Length > 0 ? "  ·  " + display.Remaining : string.Empty);
            bar.IsIndeterminate = display.Percent is null;
            if (display.Percent is double position) bar.Value = position;
        });
        dialog.Closing += (_, args) =>
        {
            if (completed) return;
            args.Cancel = true;
            if (!allowCancellation || cancellation.IsCancellationRequested) return;
            progress.Dispose();
            cancellation.Cancel();
            dialog.CloseButtonText = string.Empty;
            status.Text = "Cancelling safely…";
            remaining.Text = "Finishing the current step and preserving recovery files.";
        };
        await _dialogGate.WaitAsync();
        Exception? failure = null;
        bool cancelled = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) => elapsed.Text = "Elapsed " + clock.Elapsed.ToString(@"hh\:mm\:ss");
        try
        {
            PrepareDialog(dialog, DesktopDialogSize.Standard);
            var show = dialog.ShowAsync();
            timer.Start();
            try
            {
                // Core workflows include synchronous validation and filesystem transactions.
                await Task.Run(() => action(progress, cancellation.Token));
                cancelled = cancellation.IsCancellationRequested;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
            catch (Exception ex) { failure = ex; diagnostics.Log(title + " failed.", ex); }
            finally
            {
                progress.Dispose();
                completed = true;
                dialog.Hide();
                await show;
            }
        }
        finally
        {
            timer.Stop();
            _dialogGate.Release();
        }
        if (failure is not null)
            await ShowMessageAsync(title + " failed", failure.Message);
        else
            notifications.Show(title, cancelled ? "Cancelled. Completed steps have been kept; any remaining work was stopped safely." : "Completed successfully.");
        return failure is null && !cancelled;
    }
}
