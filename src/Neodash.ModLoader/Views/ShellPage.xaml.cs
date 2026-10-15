using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Views;

public sealed partial class ShellPage : Page
{
    private ShellController? _controller;
    private bool _isHandlingSelection;
    private NavigationViewItem? _lastItem;
    public ShellPage()
    {
        InitializeComponent();
        Loaded += ShellPage_Loaded;
    }

    internal void Activate(ShellController controller)
    {
        _controller = controller;
        controller.Attach(ContentFrame);
        if (IsLoaded)
            SelectInitialPage();
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e) => SelectInitialPage();
    private void SelectInitialPage()
    {
        if (_controller is null || _lastItem is not null)
            return;
        NavView.SelectedItem = _controller.InitialPage == AppPage.Home ? NavView.MenuItems[0] : NavView.FooterMenuItems[0];
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isHandlingSelection || _controller is null)
            return;
        _isHandlingSelection = true;
        try
        {
            if (_controller.IsBlocked || !await _controller.CanLeaveAsync())
            {
                NavView.SelectedItem = _lastItem;
                return;
            }

            if (args.SelectedItem is not NavigationViewItem item)
                return;
            AppPage? destination = item.Tag switch
            {
                "Home" => AppPage.Home,
                "ModLibrary" => AppPage.ModLibrary,
                "Settings" => AppPage.Settings,
                _ => null
            };
            if (destination is null)
                return;
            _controller.Navigate(destination.Value);
            _lastItem = item;
        }
        finally
        {
            _isHandlingSelection = false;
        }
    }
}
