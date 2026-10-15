using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Neodash_Mod_Loader.Services;
using Neodash_Mod_Loader.Ui;
using Microsoft.UI.Xaml.Media;
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
        private readonly IAppNavigator _navigator;
        private readonly INavigationGuard _navigationGuard;
        private readonly StartupCoordinator _startup;
        private readonly AppUpdateCoordinator _updates;
        public MainWindow(ISettingsService settings, IWindowContext window, IAppNavigator navigator, INavigationGuard navigationGuard, StartupCoordinator startup, AppUpdateCoordinator updates, IAppDiagnostics diagnostics, IUserNotifications notifications, StartupWindowReveal startupReveal)
        {
            _navigator = navigator;
            _navigationGuard = navigationGuard;
            _startup = startup;
            _updates = updates;
            startupReveal.Prepare(this);
            InitializeComponent();
            startupReveal.WaitForContent(RootGrid, () => navigator.IsContentReady, () => _ = CheckForUpdatesAsync());
            window.Attach(this, RootGrid);
            notifications.Published += ShowNotification;
            Closed += (_, _) => notifications.Published -= ShowNotification;
            window.ApplyTheme(settings.Theme);
            navigator.AttachRoot(RootFrame);
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            TitleBarVersionText.Text = diagnostics.Version is { } version ? "v" + version.ToString(3) : string.Empty;
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
                if (args.DidPositionChange || args.DidSizeChange)
                    ApplyMinimumWindowSize();
            };
        }

        private void ShowNotification(object? sender, UserNotification notification)
        {
            OperationNotice.Title = notification.Title;
            OperationNotice.Message = notification.Message;
            OperationNotice.IsOpen = true;
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
            if (RootGrid.XamlRoot is not { } root)
                return;
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
            if (_navigationGuard.IsBlocked)
            {
                args.Cancel = true;
                return;
            }

            if (!_navigationGuard.HasUnsavedChanges)
                return;
            args.Cancel = true;
            if (await _navigationGuard.ResolveUnsavedChangesAsync("You have unsaved mod changes. Would you like to save them before exiting?"))
            {
                AppWindow.Closing -= AppWindow_Closing;
                Close();
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
            if (Math.Abs(scale - _minSizeScale) < DpiScaleTolerance && workArea.Width == _minSizeWorkArea.Width && workArea.Height == _minSizeWorkArea.Height)
                return;
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
            CenterWindowInWorkArea();
            RootGrid.XamlRoot.Changed += (s, args) =>
            {
                ApplyMinimumWindowSize();
                UpdateTitleBarLayout();
            };
            UpdateTitleBarLayout();
            _navigator.NavigateRoot(_startup.GetInitialPage());
        }

        private void CenterWindowInWorkArea()
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;
            int width = Math.Min(AppWindow.Size.Width, workArea.Width);
            int height = Math.Min(AppWindow.Size.Height, workArea.Height);
            // WorkArea coordinates are relative to this display, including its taskbar offset.
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                workArea.X + (workArea.Width - width) / 2,
                workArea.Y + (workArea.Height - height) / 2,
                width, height), display);
        }

        public Task CheckForUpdatesAsync(bool showUpToDate = false) => _updates.CheckAsync(showUpToDate);
        public void SetTitleBarBackground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            AppTitleBar.Background = brush;
        }
    }
}
