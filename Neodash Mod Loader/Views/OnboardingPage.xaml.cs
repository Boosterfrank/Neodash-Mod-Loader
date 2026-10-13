using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;

namespace Neodash_Mod_Loader.Views
{
    public sealed partial class OnboardingPage : Page
    {
        private string _gameWin64Path;
        private readonly string _downloadUrl = "https://www.dropbox.com/scl/fi/ewqe9uccnmeffxdsdc4v1/Win64Data.zip?rlkey=f7n9doxpzkgoocwdwo5ot8b9j&st=g5vf5z3s&dl=1";

        public OnboardingPage()
        {
            this.InitializeComponent();
            _gameWin64Path = GamePathResolver.GetNeodashWin64Path();
            this.Loaded += OnboardingPage_Loaded;
            this.Unloaded += OnboardingPage_Unloaded;
        }

        private void OnboardingPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                // Set the title bar to a different color (e.g. solid dark gray) during onboarding
                mainWindow.SetTitleBarBackground(new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32)));
            }
        }

        private void OnboardingPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                // Reset to transparent
                mainWindow.SetTitleBarBackground(new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent));
            }
        }

        private async void StartSetupButton_Click(object sender, RoutedEventArgs e)
        {
            string ue4ssDllPath = Path.Combine(_gameWin64Path, "UE4SS.dll");

            if (File.Exists(ue4ssDllPath))
            {
                var dialog = new ContentDialog
                {
                    Title = "Warning",
                    Content = "UE4SS is already installed. Continuing will override all existing UE4SS settings.",
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                int countdown = 3;
                dialog.PrimaryButtonText = $"OK ({countdown})";
                dialog.IsPrimaryButtonEnabled = false;

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (s, ev) =>
                {
                    countdown--;
                    if (countdown > 0)
                    {
                        dialog.PrimaryButtonText = $"OK ({countdown})";
                    }
                    else
                    {
                        dialog.PrimaryButtonText = "OK";
                        dialog.IsPrimaryButtonEnabled = true;
                        timer.Stop();
                    }
                };
                timer.Start();

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            WelcomePanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;

            try
            {
                await InstallUE4SSAsync();
                
                LoadingText.Text = "Installing CustomServer...";
                await InstallCustomServerAsync();
                
                LoadingText.Text = "Done!";
                await Task.Delay(1000);

                LoadingPanel.Visibility = Visibility.Collapsed;
                FinishedPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Installation Error",
                    Content = $"An error occurred during setup: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
                WelcomePanel.Visibility = Visibility.Visible;
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task InstallUE4SSAsync()
        {
            if (!Directory.Exists(_gameWin64Path))
            {
                Directory.CreateDirectory(_gameWin64Path);
            }

            string tempZipPath = Path.Combine(Path.GetTempPath(), "Win64Data.zip");

            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(_downloadUrl);
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }

            // Backup existing mods.txt if it exists
            string modsTxtDest = Path.Combine(_gameWin64Path, @"Mods\mods.txt");
            string modsTxtBackup = null;
            if (File.Exists(modsTxtDest))
            {
                modsTxtBackup = Path.Combine(Path.GetTempPath(), "mods_backup.txt");
                File.Copy(modsTxtDest, modsTxtBackup, true);
            }

            ZipFile.ExtractToDirectory(tempZipPath, _gameWin64Path, overwriteFiles: true);

            // Restore mods.txt if we backed it up
            if (modsTxtBackup != null && File.Exists(modsTxtBackup))
            {
                File.Copy(modsTxtBackup, modsTxtDest, true);
                File.Delete(modsTxtBackup);
            }

            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }

        private async Task InstallCustomServerAsync()
        {
            try
            {
                using var client = new HttpClient();
                string json = await client.GetStringAsync("https://neodashweb.pages.dev/modloader/mods/modlist.json");
                var response = System.Text.Json.JsonSerializer.Deserialize<Models.ModLibraryResponse>(json);
                
                var customServerMod = response?.Mods?.Find(m => m.Name.Equals("Neodash Custom Server", StringComparison.OrdinalIgnoreCase));
                if (customServerMod != null && !string.IsNullOrEmpty(customServerMod.DownloadURL))
                {
                    string tempZipPath = Path.Combine(Path.GetTempPath(), "lib_mod_onboarding.zip");
                    var downloadResponse = await client.GetAsync(customServerMod.DownloadURL);
                    downloadResponse.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await downloadResponse.Content.CopyToAsync(fs);
                    }

                    ModManager.InstallModZip(_gameWin64Path, tempZipPath);

                    if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

                    SettingsManager.Values[$"InstalledMod_{customServerMod.Name}"] = customServerMod.Version;
                    SettingsManager.SaveSettings();
                }
            }
            catch (Exception)
            {
                // Silently ignore custom server installation failure during onboarding if it fails
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.Values["IsSetupComplete"] = true;
            SettingsManager.SaveSettings();
            if (App.MainWindow.Content is FrameworkElement rootElement && rootElement.FindName("RootFrame") is Frame rootFrame)
            {
                rootFrame.Navigate(typeof(ShellPage), null, new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
            }
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsManager.Values["IsSetupComplete"] = true;
            SettingsManager.SaveSettings();
            if (App.MainWindow.Content is FrameworkElement rootElement && rootElement.FindName("RootFrame") is Frame rootFrame)
            {
                rootFrame.Navigate(typeof(ShellPage), null, new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
            }
        }
    }
}
