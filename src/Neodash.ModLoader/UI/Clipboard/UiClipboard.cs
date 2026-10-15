using Neodash_Mod_Loader.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Neodash_Mod_Loader;

public sealed class UiClipboard : IDesktopClipboard
{
    public void SetText(string text)
    {
        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetText(text);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }
}
