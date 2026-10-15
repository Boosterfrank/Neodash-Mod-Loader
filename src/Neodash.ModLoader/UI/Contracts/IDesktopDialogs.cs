using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;
public interface IDesktopDialogs : IUserDialogs
{
    Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, DesktopDialogSize size = DesktopDialogSize.Standard);
}
