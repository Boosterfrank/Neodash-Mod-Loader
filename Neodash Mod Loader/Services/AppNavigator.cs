using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Neodash_Mod_Loader.Views;

namespace Neodash_Mod_Loader.Services;

public sealed partial class AppNavigator(IPageActivation activation) : IAppNavigator, IDisposable
{
    private Frame? _root;
    private Frame? _shell;
    public bool IsContentReady => IsReady(_root) && (_root?.Content is not ShellPage || IsReady(_shell));

    private static bool IsReady(Frame? frame) => frame is { IsLoaded: true,
        Content: FrameworkElement { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } };
    public void AttachRoot(Frame frame) => Attach(ref _root, frame);
    public void AttachShell(Frame frame) => Attach(ref _shell, frame);
    public void NavigateRoot(AppPage destination) => Navigate(_root, destination);
    public void NavigateShell(AppPage destination) => Navigate(_shell, destination);
    private void Attach(ref Frame? current, Frame frame)
    {
        if (ReferenceEquals(current, frame))
            return;
        if (current is not null)
            current.Navigated -= FrameNavigated;
        current = frame;
        current.Navigated += FrameNavigated;
        if (current.Content is Page page)
            activation.Activate(page);
    }

    private void FrameNavigated(object sender, NavigationEventArgs args)
    {
        if (args.Content is Page page)
            activation.Activate(page);
    }

    private static void Navigate(Frame? frame, AppPage destination)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Type pageType = destination switch
        {
            AppPage.Home => typeof(HomePage),
            AppPage.ModLibrary => typeof(ModLibraryPage),
            AppPage.Settings => typeof(SettingsPage),
            AppPage.Onboarding => typeof(OnboardingPage),
            AppPage.Shell => typeof(ShellPage),
            _ => throw new ArgumentOutOfRangeException(nameof(destination))};
        // The first page must be painted fully before the native window is revealed.
        // Later navigation keeps its normal transition.
        NavigationTransitionInfo? transition = frame.Content is null ? new SuppressNavigationTransitionInfo() : null;
        if (!frame.Navigate(pageType, null, transition))
            throw new InvalidOperationException($"Could not navigate to {destination}.");
    }

    public void Dispose()
    {
        if (_root is not null)
            _root.Navigated -= FrameNavigated;
        if (_shell is not null)
            _shell.Navigated -= FrameNavigated;
        _root = null;
        _shell = null;
    }
}
