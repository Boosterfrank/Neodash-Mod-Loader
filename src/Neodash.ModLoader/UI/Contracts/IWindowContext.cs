using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;
public interface IWindowContext : IInstallerLauncher
{
    WindowId WindowId { get; }

    XamlRoot XamlRoot { get; }

    void Attach(Window window, FrameworkElement root);
    void ApplyTheme(string? theme);
    void Post(Action action);
}
