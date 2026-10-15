using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;
public interface IAppNavigator
{
    bool IsContentReady { get; }

    void AttachRoot(Frame frame);
    void AttachShell(Frame frame);
    void NavigateRoot(AppPage destination);
    void NavigateShell(AppPage destination);
}
