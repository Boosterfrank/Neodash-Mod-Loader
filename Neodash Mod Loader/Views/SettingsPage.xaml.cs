using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Views;

public sealed partial class SettingsPage : Page
{
    private bool _initializing = true;
    private SettingsPageController? _controller;
    private SettingsPageController Controller => _controller ?? throw new InvalidOperationException("Settings has not been activated.");

    public SettingsPage()
    {
        InitializeComponent();
        CheckUpdatesButton.Click += CheckUpdatesButton_Click;
        Loaded += (_, _) =>
        {
            if (_controller is not null)
                RefreshStatus();
        };
    }

    internal void Activate(SettingsPageController controller)
    {
        _controller = controller;
        _initializing = true;
        SelectTheme(controller.Theme);
        _initializing = false;
        RefreshStatus();
    }

    private void SelectTheme(string? theme)
    {
        foreach (ComboBoxItem item in ThemeComboBox.Items)
            if ((string)item.Tag == theme)
            {
                ThemeComboBox.SelectedItem = item;
                return;
            }

        ThemeComboBox.SelectedIndex = 1;
    }

    private void RefreshStatus()
    {
        var state = Controller.GetState();
        string path = state.GamePath;
        GamePathText.Text = path.Length > 0 ? path : "Neodash was not found. Choose its installation folder.";
        bool hasGame = state.HasGame;
        OpenGameFolderButton.IsEnabled = hasGame;
        InstallUe4ssButton.IsEnabled = hasGame;
        RemoveUe4ssButton.IsEnabled = hasGame;
        EditUe4ssConfigButton.IsEnabled = state.HasRuntimeConfiguration;
        Ue4ssLogButton.IsEnabled = hasGame;
        Ue4ssStatusText.Text = state.RuntimeStatus;
        SettingsErrorText.Text = state.LoadError ?? string.Empty;
        SettingsRecoveryBar.IsOpen = state.LoadError is not null;
        SettingsControls.IsEnabled = state.LoadError is null;
        AppVersionText.Text = "Version " + state.Version;
    }

    private async void RetrySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller.IsBusy)
            return;
        try
        {
            Controller.RetryLoading();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _controller is null || ThemeComboBox.SelectedItem is not ComboBoxItem item)
            return;
        string? previous = Controller.Theme;
        try
        {
            Controller.SetTheme((string)item.Tag);
        }
        catch (Exception ex)
        {
            _initializing = true;
            try
            {
                SelectTheme(previous);
            }
            finally
            {
                _initializing = false;
            }

            await ShowErrorAsync(ex);
        }
    }

    private Task ShowErrorAsync(Exception ex) => Controller.ShowErrorAsync(ex);
    private async void ChooseGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller.IsBusy)
            return;
        try
        {
            if (await Controller.ChooseFolderAsync())
                RefreshStatus();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async void DetectGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (Controller.IsBusy)
            return;
        try
        {
            Controller.DetectGame();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async void InstallUe4ssButton_Click(object sender, RoutedEventArgs e)
    {
        await RunActionAsync(Controller.InstallRuntimeAsync, refresh: true);
    }

    private async void UninstallUe4ssButton_Click(object sender, RoutedEventArgs e)
    {
        await RunActionAsync(Controller.RemoveRuntimeAsync, refresh: true);
    }

    private async void OpenPathButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string tag = (string)((Button)sender).Tag;
            Controller.OpenLocation(tag);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    private async void EditUe4ssConfigButton_Click(object sender, RoutedEventArgs e)
    {
        await RunActionAsync(Controller.EditRuntimeAsync);
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await Controller.CheckUpdatesAsync();
    }

    private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
    {
        await RunActionAsync(Controller.RestartSetupAsync);
    }

    private async Task RunActionAsync(Func<Task> action, bool refresh = false)
    {
        if (Controller.IsBusy)
            return;
        try
        {
            await action();
            if (refresh)
                RefreshStatus();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }
}
