using Microsoft.Windows.Storage.Pickers;
using System.Diagnostics;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader;

public sealed class UiFiles(IWindowContext window) : IDesktopFiles
{
    public async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(window.WindowId);
        foreach (string extension in extensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickFolderAsync() =>
        (await new FolderPicker(window.WindowId).PickSingleFolderAsync())?.Path;

    public async Task<string?> SaveFileAsync(string suggestedName, string extension)
    {
        var picker = new FileSavePicker(window.WindowId) { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("File", new List<string> { extension });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public void OpenPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The file or folder does not exist.", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
