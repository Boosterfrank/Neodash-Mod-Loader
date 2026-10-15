using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Views;

public sealed partial class OnboardingPage : Page
{
    private bool _running;
    private OnboardingController? _controller;
    private OnboardingController Controller => _controller ?? throw new InvalidOperationException("Setup has not been activated.");

    public OnboardingPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_controller is null)
                return;
            UpdateGamePath();
            (StartSetupButton.IsEnabled ? StartSetupButton : ChooseGameButton).Focus(FocusState.Programmatic);
        };
    }

    internal void Activate(OnboardingController controller)
    {
        _controller = controller;
        UpdateGamePath();
        if (IsLoaded)
            (StartSetupButton.IsEnabled ? StartSetupButton : ChooseGameButton).Focus(FocusState.Programmatic);
    }

    private void UpdateGamePath()
    {
        string path = Controller.GamePath;
        GamePathText.Text = path.Length > 0 ? path : "Neodash was not found. Choose its installation folder.";
        StartSetupButton.IsEnabled = path.Length > 0;
    }

    private async void ChooseGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;
        try
        {
            if (!await Controller.ChooseFolderAsync())
                return;
            UpdateGamePath();
            StartSetupButton.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            await ErrorAsync(ex.Message);
        }
    }

    private Task ErrorAsync(string message) => Controller.ShowErrorAsync(message);
    private async void StartSetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running || Controller.IsBusy)
            return;
        _running = true;
        WelcomePanel.IsEnabled = false;
        bool installRecommended = RecommendedModsCheckBox.IsChecked == true;
        try
        {
            string? result = await Controller.RunSetupAsync(installRecommended);
            if (result is null)
                return;
            WelcomePanel.Visibility = Visibility.Collapsed;
            FinishedPanel.Visibility = Visibility.Visible;
            SetupResultText.Text = result;
            FinishButton.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            await ErrorAsync(ex.Message);
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
        if (_running || Controller.IsBusy)
            return;
        try
        {
            Controller.Complete();
        }
        catch (Exception ex)
        {
            await ErrorAsync(ex.Message);
        }
    }
}
