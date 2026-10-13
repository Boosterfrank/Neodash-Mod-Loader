using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;

public interface IWindowContext
{
    WindowId WindowId { get; }

    XamlRoot XamlRoot { get; }

    void Attach(Window window, FrameworkElement root);
    void ApplyTheme(string? theme);
    void OpenInstaller(string path);
    void Post(Action action);
}

public interface IDesktopDialogs : IUserDialogs
{
    Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog);
}

public interface IConfigurationEditor
{
    Task EditModAsync(string name);
    Task EditRuntimeAsync();
}

public interface IAppNavigator
{
    void AttachRoot(Frame frame);
    void AttachShell(Frame frame);
    void NavigateRoot(AppPage destination);
    void NavigateShell(AppPage destination);
}

public interface IPageActivation
{
    void Activate(Page page);
}
