using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Neodash_Mod_Loader.Views;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Neodash_Mod_Loader
{
    public sealed partial class MainWindow : Window
    {
        // Smallest size, in effective pixels, where the Home page header buttons and the onboarding screen still fit.
        private const int MinWindowWidth = 1100;
        private const int MinWindowHeight = 640;

        private double _minSizeScale;

        public MainWindow()
        {
            this.InitializeComponent();
            
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Set the window icon
            this.AppWindow.SetIcon("neodash mod loader logo no bg.ico");
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1720, 735));
            this.AppWindow.Closing += AppWindow_Closing;
        }

        private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
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
                
                var result = await dialog.ShowAsync();
                
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
            double scale = RootGrid.XamlRoot.RasterizationScale;
            if (scale == _minSizeScale || this.AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }
            _minSizeScale = scale;

            // PreferredMinimum* is in physical pixels and isn't rescaled when the DPI changes, so convert it here.
            // Capped to the work area so the window can't be forced larger than a small or heavily scaled screen.
            var workArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
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
            RootGrid.XamlRoot.Changed += (s, args) => ApplyMinimumWindowSize();

            // Apply saved theme
            if (SettingsManager.Values.TryGetValue("AppTheme", out object themeObj))
            {
                string theme = themeObj as string;
                RootGrid.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Dark
                };
            }
            else
            {
                RootGrid.RequestedTheme = ElementTheme.Dark;
            }
            
            // Check if setup is complete
            bool isSetupComplete = SettingsManager.Values.TryGetValue("IsSetupComplete", out object setupObj) && setupObj is bool b ? b : false;

            if (!isSetupComplete)
            {
                string dllPath = System.IO.Path.Combine(GamePathResolver.GetNeodashWin64Path(), "UE4SS.dll");
                if (System.IO.File.Exists(dllPath))
                {
                    isSetupComplete = true;
                    SettingsManager.Values["IsSetupComplete"] = true;
                    SettingsManager.SaveSettings();
                }
            }

            if (isSetupComplete)
            {
                RootFrame.Navigate(typeof(ShellPage));
            }
            else
            {
                RootFrame.Navigate(typeof(OnboardingPage));
            }
            
            _ = CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                string verStr = await client.GetStringAsync("https://neodashweb.pages.dev/modloader/download/ver.txt");
                if (Version.TryParse(verStr.Trim(), out Version onlineVersion))
                {
                    Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    
                    if (onlineVersion > currentVersion)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Update Available",
                            Content = "Downloading update...",
                            XamlRoot = RootGrid.XamlRoot
                        };

                        _ = dialog.ShowAsync();

                        var response = await client.GetAsync("https://www.dropbox.com/scl/fi/6jdl7r2yg9m3c9fkc6tgm/ModLoaderSetup.exe?rlkey=ovtja9dcb5qp6gy1nyj28u9pu&st=fe6z5awc&dl=1");
                        response.EnsureSuccessStatusCode();

                        string tempPath = System.IO.Path.GetTempPath();
                        string installerPath = System.IO.Path.Combine(tempPath, "ModLoaderSetup.exe");

                        using (var fs = new System.IO.FileStream(installerPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        // Launch the installer
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = installerPath,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(processInfo);

                        // Exit the current app so the installer can overwrite files
                        Application.Current.Exit();
                    }
                }
            }
            catch (Exception)
            {
                // Silently ignore network or parsing errors
            }
        }

        public void SetTitleBarBackground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            AppTitleBar.Background = brush;
        }
    }
}
