using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Views;

public sealed partial class OnboardingPage : Page
{
    private bool _running;
    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateGamePath();
            (StartSetupButton.IsEnabled ? StartSetupButton : ChooseGameButton).Focus(FocusState.Programmatic);
        };
    }

    private void UpdateGamePath()
    {
        string path = GamePathResolver.GetNeodashWin64Path();
        GamePathText.Text = path.Length > 0 ? path : "Neodash was not found. Choose its installation folder.";
        StartSetupButton.IsEnabled = path.Length > 0;
    }

    private async void ChooseGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        try
        {
            string? chosen = await UiFiles.PickFolderAsync();
            if (chosen is null) return;
            string path = GamePathResolver.NormalizeGameDirectory(chosen) ?? throw new IOException("Choose the Neodash installation folder or the Win64 folder containing Runway-Win64-Shipping.exe.");
            var before = SettingsManager.CaptureSnapshot();
            SettingsManager.Current.GameDirectory = path;
            SettingsManager.Current.ModsTxtMigrated = false;
            try { SettingsManager.SaveSettings(); }
            catch { SettingsManager.RestoreSnapshot(before, persist: false); throw; }
            UpdateGamePath();
            StartSetupButton.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { await ErrorAsync(ex.Message); }
    }

    private async Task ErrorAsync(string message) => await UiOperations.ShowMessageAsync(XamlRoot, "Setup could not complete", message);

    private async void StartSetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running || ModOperations.IsBusy) return;
        _running = true;
        WelcomePanel.IsEnabled = false;
        bool installRecommended = RecommendedModsCheckBox.IsChecked == true;
        try
        {
            if (!await UiOperations.RunAsync(XamlRoot, "Install UE4SS", (p, t) => Ue4SsService.InstallAsync(p, t))) return;
            string result;
            if (installRecommended)
            {
                bool recommended = await UiOperations.RunAsync(XamlRoot, "Install CustomServer", async (p, t) =>
                {
                    var catalog = await ModOperations.LoadCatalogAsync(t);
                    var mod = catalog.Mods?.Find(m => m.Name.Equals("Neodash Custom Server", StringComparison.OrdinalIgnoreCase))
                        ?? throw new IOException("CustomServer was not found in the library. You can install it later from Mod Library.");
                    await ModOperations.InstallCatalogAsync(mod, p, t);
                });
                result = recommended ? "UE4SS and CustomServer are installed." : "UE4SS is installed. CustomServer was not installed; retry it in Mod Library.";
            }
            else result = "UE4SS is installed. You can install recommended mods later.";
            WelcomePanel.Visibility = Visibility.Collapsed;
            FinishedPanel.Visibility = Visibility.Visible;
            SetupResultText.Text = result;
            FinishButton.Focus(FocusState.Programmatic);
        }
        finally
        {
            _running = false;
            WelcomePanel.IsEnabled = true;
            if (WelcomePanel.Visibility == Visibility.Visible)
            {
                UpdateGamePath();
                (StartSetupButton.IsEnabled ? StartSetupButton : ChooseGameButton).Focus(FocusState.Programmatic);
            }
        }
    }

    private async void CompleteSetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running || ModOperations.IsBusy) return;
        try
        {
            bool previous = SettingsManager.Current.IsSetupComplete;
            SettingsManager.Current.IsSetupComplete = true;
            try { SettingsManager.SaveSettings(); }
            catch { SettingsManager.Current.IsSetupComplete = previous; throw; }
            if (App.MainWindow.Content is FrameworkElement root && root.FindName("RootFrame") is Frame frame) frame.Navigate(typeof(ShellPage));
        }
        catch (Exception ex) { await ErrorAsync(ex.Message); }
    }
}
