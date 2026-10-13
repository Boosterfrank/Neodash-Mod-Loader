using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Storage;

namespace Neodash_Mod_Loader.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadTheme();
        }

        private void LoadTheme()
        {
            // Initialize Theme selection based on saved settings
            if (SettingsManager.Values.TryGetValue("AppTheme", out object themeObj))
            {
                string theme = themeObj as string;
                foreach (ComboBoxItem item in ThemeComboBox.Items)
                {
                    if ((string)item.Tag == theme)
                    {
                        ThemeComboBox.SelectedItem = item;
                        return;
                    }
                }
            }
            
            // Default selection
            ThemeComboBox.SelectedIndex = 1; // Default to Dark
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string theme = (string)selectedItem.Tag;
                
                // Save setting
                SettingsManager.Values["AppTheme"] = theme;
                SettingsManager.SaveSettings();

                // Apply theme
                if (App.MainWindow.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = theme switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                }
            }
        }

        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Reset Application",
                Content = "Are you sure you want to reset? This is for developers only and will force the onboarding screen to appear again.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                SettingsManager.Values["IsSetupComplete"] = false;
                SettingsManager.SaveSettings();
                
                if (App.MainWindow.Content is FrameworkElement rootElement && rootElement.FindName("RootFrame") is Frame rootFrame)
                {
                    rootFrame.Navigate(typeof(OnboardingPage));
                }
            }
        }
    }
}
