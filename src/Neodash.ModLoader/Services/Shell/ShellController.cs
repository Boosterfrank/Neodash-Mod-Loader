using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;

public sealed class ShellController(IAppNavigator navigator, INavigationGuard guard, IGameService game)
{
    public AppPage InitialPage => game.GetPath().Length > 0 ? AppPage.Home : AppPage.Settings;
    public bool IsBlocked => guard.IsBlocked;

    public void Attach(Frame frame) => navigator.AttachShell(frame);
    public Task<bool> CanLeaveAsync() => guard.ResolveUnsavedChangesAsync("You have unsaved mod changes. Would you like to save them before leaving?");
    public void Navigate(AppPage destination) => navigator.NavigateShell(destination);
}
