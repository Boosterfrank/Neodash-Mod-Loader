using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader;

public sealed class UiConfigurationEditor(IConfigurationService configurations, IRuntimeService runtime,
    IGameService game, IDesktopDialogs dialogs, IAppDiagnostics diagnostics, IUserNotifications notifications) : IConfigurationEditor
{
    public Task EditModAsync(string name) => EditAsync(configurations.GetModConfiguration(name));
    public Task EditRuntimeAsync() => EditAsync(runtime.GetConfiguration());

    private async Task EditAsync(ConfigurationLocation location)
    {
        var (gameRoot, folderRoot, files) = location;
        if (files.Count == 0) throw new FileNotFoundException("No configuration files were found.");
        game.EnsureClosed(gameRoot);
        using var lifetime = new CancellationTokenSource();
        CancellationToken token = lifetime.Token;
        var original = new Dictionary<string, ConfigFileDocument>(StringComparer.OrdinalIgnoreCase);
        var edited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var picker = new ComboBox
        {
            Header = "Configuration file", ItemsSource = files.Select(path => Path.GetRelativePath(folderRoot, path)).ToList(),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var editor = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 200, Height = 320,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), IsEnabled = false
        };
        var status = new TextBlock { Text = "Loading configuration…", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetName(picker, "Configuration file");
        AutomationProperties.SetName(editor, "Configuration contents");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        string? selected = null;
        bool saving = false;
        bool closed = false;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(picker); content.Children.Add(editor); content.Children.Add(status);
        var dialog = new ContentDialog
        {
            Title = "Edit configuration", Content = content, IsPrimaryButtonEnabled = false,
            PrimaryButtonText = "Save changes", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
        };

        async Task LoadSelectedAsync()
        {
            if (selected is not null && editor.IsEnabled) edited[selected] = editor.Text;
            picker.IsEnabled = false;
            editor.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            status.Text = "Loading configuration…";
            try
            {
                string target = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, files[picker.SelectedIndex]));
                if (!original.ContainsKey(target))
                {
                    var document = await Task.Run(async () =>
                    {
                        if (new FileInfo(target).Length > 4_194_304)
                            throw new IOException("This configuration is too large for the built-in editor. Open its folder to edit it externally.");
                        return ConfigFileDocument.Load(target, await File.ReadAllBytesAsync(target, token));
                    }, token);
                    if (closed) return;
                    original[target] = document;
                    edited[target] = document.Text;
                }
                selected = target;
                editor.Text = edited[target];
                editor.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                status.Text = "Edits are kept when switching files. Save applies all changed files.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Dialog closed. */ }
            catch (Exception ex)
            {
                if (!closed) status.Text = ex.Message;
                diagnostics.Log("Could not open configuration.", ex);
            }
            finally { if (!closed) picker.IsEnabled = true; }
        }

        dialog.Opened += async (_, _) => await LoadSelectedAsync();
        picker.SelectionChanged += async (_, _) => await LoadSelectedAsync();
        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            saving = true;
            picker.IsEnabled = false;
            editor.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.CloseButtonText = string.Empty;
            status.Text = "Saving configuration…";
            try
            {
                if (selected is not null) edited[selected] = editor.Text;
                var changes = edited.Select(item => new ConfigFileChange(original[item.Key], item.Value))
                    .Where(change => !string.Equals(change.Text, change.Document.Text, StringComparison.Ordinal)).ToList();
                await Task.Run(() =>
                {
                    game.EnsureClosed(gameRoot);
                    foreach (var change in changes)
                        _ = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, change.Document.Path));
                    ConfigFileTransactions.Apply(changes);
                });
                if (changes.Count > 0)
                    diagnostics.Log("Saved configuration: " + string.Join(", ", changes.Select(change => Path.GetFileName(change.Document.Path))));
                notifications.Show("Configuration saved", changes.Count == 0 ? "No changes were needed." : $"Saved {changes.Count} configuration file(s).");
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                diagnostics.Log("Could not save configuration.", ex);
                status.Text = "Could not save. Your edits are still here.\n" + ex.Message;
            }
            finally
            {
                saving = false;
                picker.IsEnabled = true;
                editor.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.CloseButtonText = "Cancel";
                deferral.Complete();
            }
        };
        await dialogs.ShowDialogAsync(dialog, DesktopDialogSize.Wide);
    }
}
