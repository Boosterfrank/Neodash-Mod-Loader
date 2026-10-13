using Microsoft.Windows.Storage.Pickers;
using System.Diagnostics;

namespace Neodash_Mod_Loader;

public static class UiFiles
{
    public static async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id);
        foreach (string extension in extensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickFolderAsync() =>
        (await new FolderPicker(App.MainWindow.AppWindow.Id).PickSingleFolderAsync())?.Path;

    public static async Task<string?> SaveFileAsync(string suggestedName, string extension)
    {
        var picker = new FileSavePicker(App.MainWindow.AppWindow.Id) { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("File", new List<string> { extension });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public static void OpenPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The file or folder does not exist.", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
