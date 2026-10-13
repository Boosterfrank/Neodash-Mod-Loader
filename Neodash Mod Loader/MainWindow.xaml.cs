using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Neodash_Mod_Loader.Views;
using Windows.UI.ViewManagement;

namespace Neodash_Mod_Loader
{
    public sealed partial class MainWindow : Window
    {
        // Smallest size, in effective pixels, where the Home page header buttons and the onboarding screen still fit.
        private const int MinWindowWidth = 1100;
        private const int MinWindowHeight = 640;
        private const double DpiScaleTolerance = 0.000001;

        private double _minSizeScale;
        private Windows.Graphics.SizeInt32 _minSizeWorkArea;
        private readonly UISettings _systemUiSettings = new();
        private readonly AccessibilitySettings _accessibilitySettings = new();

        public MainWindow()
        {
            this.InitializeComponent();
            RootGrid.RequestedTheme = SettingsManager.Current.AppTheme switch
            {
                "Light" => ElementTheme.Light,
                "Default" => ElementTheme.Default,
                _ => ElementTheme.Dark
            };
            
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            TitleBarVersionText.Text = typeof(App).Assembly.GetName().Version is { } version ? "v" + version.ToString(3) : string.Empty;
            RootGrid.ActualThemeChanged += (_, _) => UpdateTitleBarAppearance();
            _systemUiSettings.ColorValuesChanged += SystemColorsChanged;
            Closed += (_, _) => _systemUiSettings.ColorValuesChanged -= SystemColorsChanged;
            UpdateTitleBarAppearance();

            // Set the window icon
            this.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "neodash mod loader logo no bg.ico"));
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1720, 735));
            this.AppWindow.Closing += AppWindow_Closing;
            this.AppWindow.Changed += (_, args) =>
            {
                if (args.DidPositionChange || args.DidSizeChange) ApplyMinimumWindowSize();
            };
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            double opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.6 : 1;
            CenteredTitle.Opacity = opacity;
            TitleBarIcon.Opacity = opacity;
        }

        private void SystemColorsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(UpdateTitleBarAppearance);

        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateTitleBarLayout();

        private void UpdateTitleBarLayout()
        {
            if (RootGrid.XamlRoot is not { } root) return;
            // Equal reserves keep the title at the window's center, including the caption-button area.
            double padding = Math.Max(AppWindow.TitleBar.LeftInset, AppWindow.TitleBar.RightInset) / root.RasterizationScale + 16;
            CenteredTitle.Margin = new Thickness(padding, 0, padding, 0);
        }

        private void UpdateTitleBarAppearance()
        {
            var bar = AppWindow.TitleBar;
            if (_accessibilitySettings.HighContrast)
            {
                BackdropTint.Background = new SolidColorBrush(_systemUiSettings.UIElementColor(UIElementType.Window));
                bar.ButtonBackgroundColor = null;
                bar.ButtonInactiveBackgroundColor = null;
                bar.ButtonHoverBackgroundColor = null;
                bar.ButtonPressedBackgroundColor = null;
                bar.ButtonForegroundColor = null;
                bar.ButtonInactiveForegroundColor = null;
                bar.ButtonHoverForegroundColor = null;
                bar.ButtonPressedForegroundColor = null;
                return;
            }
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            BackdropTint.Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(96, 0, 0, 0) : Microsoft.UI.Colors.Transparent);
            var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            byte hoverChannel = dark ? (byte)48 : (byte)229;
            byte pressedChannel = dark ? (byte)64 : (byte)204;
            bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, hoverChannel, hoverChannel, hoverChannel);
            bar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, pressedChannel, pressedChannel, pressedChannel);
            bar.ButtonForegroundColor = foreground;
            bar.ButtonHoverForegroundColor = foreground;
            bar.ButtonPressedForegroundColor = foreground;
            bar.ButtonInactiveForegroundColor = _systemUiSettings.UIElementColor(UIElementType.GrayText);
        }

        private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (ModOperations.IsBusy || UiOperations.IsDialogOpen)
            {
                args.Cancel = true;
                return;
            }
            if (HomePage.Current != null && HomePage.Current.HasUnsavedChanges)
            {
                args.Cancel = true;

                var dialog = new ContentDialog
                {
                    Title = "Unsaved Changes",
                    Content = "You have unsaved mod changes. Would you like to save them before exiting?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Discard",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot
                };
                
                var result = await UiOperations.ShowDialogAsync(dialog);
                
                if (result == ContentDialogResult.Primary)
                {
                    // If some mods couldn't be moved, SaveModsAsync has already explained why; keep the window open.
                    if (await HomePage.Current.SaveModsAsync())
                    {
                        this.AppWindow.Closing -= AppWindow_Closing;
                        this.Close();
                    }
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    this.AppWindow.Closing -= AppWindow_Closing;
                    this.Close();
                }
            }
        }

        private void ApplyMinimumWindowSize()
        {
            if (RootGrid.XamlRoot is not { } root || this.AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }
            double scale = root.RasterizationScale;
            var workArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            if (Math.Abs(scale - _minSizeScale) < DpiScaleTolerance && workArea.Width == _minSizeWorkArea.Width && workArea.Height == _minSizeWorkArea.Height) return;
            _minSizeScale = scale;
            _minSizeWorkArea = new Windows.Graphics.SizeInt32(workArea.Width, workArea.Height);

            // PreferredMinimum* is in physical pixels and isn't rescaled when the DPI changes, so convert it here.
            // Capped to the work area so the window can't be forced larger than a small or heavily scaled screen.
            int minWidth = Math.Min((int)Math.Ceiling(MinWindowWidth * scale), workArea.Width);
            int minHeight = Math.Min((int)Math.Ceiling(MinWindowHeight * scale), workArea.Height);

            presenter.PreferredMinimumWidth = minWidth;
            presenter.PreferredMinimumHeight = minHeight;

            var size = this.AppWindow.Size;
            if (presenter.State == OverlappedPresenterState.Restored && (size.Width < minWidth || size.Height < minHeight))
            {
                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(size.Width, minWidth), Math.Max(size.Height, minHeight)));
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyMinimumWindowSize();
            RootGrid.XamlRoot.Changed += (s, args) =>
            {
                ApplyMinimumWindowSize();
                UpdateTitleBarLayout();
            };
            UpdateTitleBarLayout();

            // Check if setup is complete
            bool isSetupComplete = SettingsManager.Current.IsSetupComplete;

            if (!isSetupComplete)
            {
                string gamePath = GamePathResolver.GetNeodashWin64Path();
                string dllPath = System.IO.Path.Combine(gamePath, "UE4SS.dll");
                if (SettingsManager.LoadError is null && gamePath.Length > 0 && (System.IO.File.Exists(dllPath) || System.IO.File.Exists(Path.Combine(gamePath, "ue4ss", "UE4SS.dll"))))
                {
                    SettingsManager.Current.IsSetupComplete = true;
                    try
                    {
                        SettingsManager.SaveSettings();
                        isSetupComplete = true;
                    }
                    catch (Exception ex)
                    {
                        SettingsManager.Current.IsSetupComplete = false;
                        AppStorage.Log("Could not save the existing UE4SS setup state; setup remains available.", ex);
                    }
                }
            }

            if (SettingsManager.LoadError is not null)
            {
                RootFrame.Navigate(typeof(SettingsPage));
            }
            else if (isSetupComplete)
            {
                RootFrame.Navigate(typeof(ShellPage));
            }
            else
            {
                RootFrame.Navigate(typeof(OnboardingPage));
            }
            
            _ = CheckForUpdatesAsync();
        }

        private bool _checkingUpdates;

        public async Task CheckForUpdatesAsync(bool showUpToDate = false)
        {
            if (_checkingUpdates || ModOperations.IsBusy) return;
            _checkingUpdates = true;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                string text = await client.GetStringAsync(Properties.ResourceStrings.GetRequired("ModLoaderVersionUrl"));
                if (!Version.TryParse(text.Trim(), out Version? onlineVersion)) throw new IOException("The update server returned an invalid version.");
                Version? currentVersion = typeof(App).Assembly.GetName().Version;
                if (onlineVersion <= currentVersion)
                {
                    if (showUpToDate) await UiOperations.ShowMessageAsync(RootGrid.XamlRoot, "No update available", "You are using the latest available version.");
                    return;
                }
                if (!await UiOperations.ConfirmAsync(RootGrid.XamlRoot, "Update available", $"Version {onlineVersion} is available. Download and open its installer?", "Download update")) return;
                if (HomePage.Current is { HasUnsavedChanges: true } home && !await home.SaveModsAsync()) return;
                string? installer = null;
                bool success = await UiOperations.RunAsync(RootGrid.XamlRoot, "Download application update", async (progress, token) =>
                {
                    installer = await ModOperations.DownloadAsync(Properties.ResourceStrings.GetRequired("ModLoaderDownloadUrl"), ".exe", progress, token);
                });
                if (!success || installer is null) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer) { UseShellExecute = true });
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                AppStorage.Log("Application update check failed.", ex);
                if (showUpToDate) await UiOperations.ShowMessageAsync(RootGrid.XamlRoot, "Update check failed", ex.Message);
            }
            finally { _checkingUpdates = false; }
        }

        public void SetTitleBarBackground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            AppTitleBar.Background = brush;
        }
    }
}
