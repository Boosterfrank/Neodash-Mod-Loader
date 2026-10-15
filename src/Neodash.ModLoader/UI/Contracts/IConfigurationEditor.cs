using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;
public interface IConfigurationEditor
{
    Task EditModAsync(string name);
    Task EditRuntimeAsync();
}
