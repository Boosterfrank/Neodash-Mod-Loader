using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Views
{
    public sealed partial class ShellPage : Page
    {
        public ShellPage()
        {
            this.InitializeComponent();
            this.Loaded += ShellPage_Loaded;
        }

        private void ShellPage_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = GamePathResolver.GetNeodashWin64Path().Length > 0 ? NavView.MenuItems[0] : NavView.FooterMenuItems[0];
        }

        private bool _isHandlingSelection;
        private NavigationViewItem? _lastItem;

        private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isHandlingSelection) return;
            if (ModOperations.IsBusy || UiOperations.IsDialogOpen)
            {
                _isHandlingSelection = true;
                if (_lastItem is not null) NavView.SelectedItem = _lastItem;
                _isHandlingSelection = false;
                return;
            }

            if (HomePage.Current != null && HomePage.Current.HasUnsavedChanges)
            {
                _isHandlingSelection = true;
                
                var dialog = new ContentDialog
                {
                    Title = "Unsaved Changes",
                    Content = "You have unsaved mod changes. Would you like to save them before leaving?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Discard",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };
                
                var result = await UiOperations.ShowDialogAsync(dialog);
                
                if (result == ContentDialogResult.Secondary)
                {
                    HomePage.Current.DiscardChanges();
                }
                else if (result == ContentDialogResult.None || !await HomePage.Current.SaveModsAsync())
                {
                    // Cancelled, or the save failed: stay on Home so the pending changes aren't lost.
                    if (_lastItem != null)
                    {
                        NavView.SelectedItem = _lastItem;
                    }
                    _isHandlingSelection = false;
                    return;
                }
                
                _isHandlingSelection = false;
            }

            NavigateToSelectedPage(args);
        }

        private void NavigateToSelectedPage(NavigationViewSelectionChangedEventArgs args)
        {
            _lastItem = args.SelectedItem as NavigationViewItem;

            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is NavigationViewItem item)
            {
                switch (item.Tag)
                {
                    case "Home":
                        ContentFrame.Navigate(typeof(HomePage));
                        break;
                    case "ModLibrary":
                        ContentFrame.Navigate(typeof(ModLibraryPage));
                        break;
                    case "Settings":
                        ContentFrame.Navigate(typeof(SettingsPage));
                        break;
                }
            }
        }
    }
}
