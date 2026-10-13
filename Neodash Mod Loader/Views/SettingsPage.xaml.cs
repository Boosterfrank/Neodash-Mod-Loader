using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Views;

public sealed partial class SettingsPage : Page
{
    private bool _initializing;
    public SettingsPage()
    {
        _initializing = true;
        InitializeComponent();
        CheckUpdatesButton.Click += CheckUpdatesButton_Click;
        SelectTheme(SettingsManager.Current.AppTheme);
        _initializing = false;
        Loaded += (_, _) => RefreshStatus();
    }

    private void SelectTheme(string? theme)
    {
        foreach (ComboBoxItem item in ThemeComboBox.Items)
            if ((string)item.Tag == theme) { ThemeComboBox.SelectedItem = item; return; }
        ThemeComboBox.SelectedIndex = 1;
    }

    private void RefreshStatus()
    {
        string path = GamePathResolver.GetNeodashWin64Path();
        GamePathText.Text = path.Length > 0 ? path : "Neodash was not found. Choose its installation folder.";
        bool hasGame = path.Length > 0;
        OpenGameFolderButton.IsEnabled = hasGame;
        InstallUe4ssButton.IsEnabled = hasGame;
        RemoveUe4ssButton.IsEnabled = hasGame;
        EditUe4ssConfigButton.IsEnabled = hasGame;
        Ue4ssLogButton.IsEnabled = hasGame;
        Ue4ssStatusText.Text = Ue4SsService.GetStatus(path);
        SettingsErrorText.Text = SettingsManager.LoadError ?? string.Empty;
        SettingsRecoveryBar.IsOpen = SettingsManager.LoadError is not null;
        SettingsControls.IsEnabled = SettingsManager.LoadError is null;
        AppVersionText.Text = "Version " + typeof(App).Assembly.GetName().Version;
    }

    private async void RetrySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy) return;
        try
        {
            SettingsManager.LoadSettings();
            RefreshStatus();
            if (SettingsManager.LoadError is not null) return;
            if (App.MainWindow.Content is FrameworkElement root)
            {
                root.RequestedTheme = SettingsManager.Current.AppTheme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
                if (root.FindName("RootFrame") is Frame frame)
                    frame.Navigate(SettingsManager.Current.IsSetupComplete ? typeof(ShellPage) : typeof(OnboardingPage));
            }
        }
        catch (Exception ex) { await ShowErrorAsync(ex); }
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeComboBox.SelectedItem is not ComboBoxItem item) return;
        string? previous = SettingsManager.Current.AppTheme;
        try
        {
            SettingsManager.Current.AppTheme = (string)item.Tag;
            SettingsManager.SaveSettings();
            if (App.MainWindow.Content is FrameworkElement root)
                root.RequestedTheme = (string)item.Tag switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        }
        catch (Exception ex)
        {
            SettingsManager.Current.AppTheme = previous;
            _initializing = true;
            try { SelectTheme(previous); }
            finally { _initializing = false; }
            await ShowErrorAsync(ex);
        }
    }

    private async Task ShowErrorAsync(Exception ex)
    {
        AppStorage.Log("Settings operation failed.", ex);
        await UiOperations.ShowMessageAsync(XamlRoot, "Could not complete action", ex.Message);
    }

    private async void ChooseGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy) return;
        try
        {
            string? chosen = await UiFiles.PickFolderAsync();
            if (chosen is null) return;
            string path = GamePathResolver.NormalizeGameDirectory(chosen) ?? throw new IOException("Choose the Neodash folder containing Runway-Win64-Shipping.exe, or its game installation root.");
            GameRuntime.EnsureGameClosed(path);
            var previous = SettingsManager.CaptureSnapshot();
            SettingsManager.Current.GameDirectory = path;
            SettingsManager.Current.ModsTxtMigrated = false;
            try { SettingsManager.SaveSettings(); }
            catch { SettingsManager.RestoreSnapshot(previous, persist: false); throw; }
            RefreshStatus();
        }
        catch (Exception ex) { await ShowErrorAsync(ex); }
    }

    private async void DetectGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy) return;
        var previous = SettingsManager.CaptureSnapshot();
        try
        {
            SettingsManager.Current.GameDirectory = null;
            if (GamePathResolver.GetNeodashWin64Path().Length == 0) throw new DirectoryNotFoundException("Neodash was not found in Steam libraries. Choose its folder manually.");
            SettingsManager.Current.ModsTxtMigrated = false;
            SettingsManager.SaveSettings();
            RefreshStatus();
        }
        catch (Exception ex) { SettingsManager.RestoreSnapshot(previous, persist: false); await ShowErrorAsync(ex); }
    }

    private async void InstallUe4ssButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy) return;
        await UiOperations.RunAsync(XamlRoot, "Install / repair UE4SS", (p, t) => Ue4SsService.InstallAsync(p, t));
        RefreshStatus();
    }

    private async void UninstallUe4ssButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy || !await UiOperations.ConfirmAsync(XamlRoot, "Remove UE4SS?", "Delete the UE4SS runtime DLLs? Your mod folders and configuration files will be kept.", "Remove UE4SS")) return;
        await UiOperations.RunAsync(XamlRoot, "Remove UE4SS", (_, _) => Ue4SsService.UninstallAsync(), allowCancellation: false);
        RefreshStatus();
    }

    private async void OpenPathButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string tag = (string)((Button)sender).Tag;
            string path = tag switch
            {
                "ue4ss" => Path.Combine(GamePathResolver.RequireGamePath(), "UE4SS.log"),
                "crash" => Path.Combine(AppStorage.DirectoryPath, "crash.txt"),
                "config" => Path.Combine(GamePathResolver.RequireGamePath(), "UE4SS-settings.ini"),
                "settings" => AppStorage.DirectoryPath,
                "backups" => AppStorage.BackupsPath,
                "folder" => GamePathResolver.RequireGamePath(),
                _ => AppStorage.LogPath
            };
            if (tag == "backups") Directory.CreateDirectory(path);
            UiFiles.OpenPath(path);
        }
        catch (Exception ex) { await ShowErrorAsync(ex); }
    }

    private async void EditUe4ssConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy) return;
        try
        {
            string path = GamePathResolver.RequireGamePath();
            await UiConfigurationEditor.EditAsync(XamlRoot, path, path, [SafePaths.Child(path, "UE4SS-settings.ini")]);
        }
        catch (Exception ex) { await ShowErrorAsync(ex); }
    }

    private static async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window) await window.CheckForUpdatesAsync(showUpToDate: true);
    }

    private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModOperations.IsBusy || !await UiOperations.ConfirmAsync(XamlRoot, "Run setup again?", "Show setup again? Existing mods, profiles, and settings will be kept.", "Show setup")) return;
        try
        {
            bool previous = SettingsManager.Current.IsSetupComplete;
            SettingsManager.Current.IsSetupComplete = false;
            try { SettingsManager.SaveSettings(); }
            catch { SettingsManager.Current.IsSetupComplete = previous; throw; }
            if (App.MainWindow.Content is FrameworkElement root && root.FindName("RootFrame") is Frame frame) frame.Navigate(typeof(OnboardingPage));
        }
        catch (Exception ex) { await ShowErrorAsync(ex); }
    }
}
