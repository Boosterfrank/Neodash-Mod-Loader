using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Neodash_Mod_Loader;

public static class UiConfigurationEditor
{
    public static string[] FindFiles(string gameRoot, string folderRoot)
    {
        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, folderRoot)));
        while (directories.Count > 0)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                string safe = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, entry));
                if (Directory.Exists(safe)) directories.Push(safe);
                else if (new[] { ".ini", ".cfg", ".toml", ".json" }.Contains(Path.GetExtension(safe), StringComparer.OrdinalIgnoreCase)) files.Add(safe);
            }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files.ToArray();
    }

    public static async Task EditAsync(XamlRoot xamlRoot, string gameRoot, string folderRoot, IReadOnlyList<string> files)
    {
        if (files.Count == 0) throw new FileNotFoundException("No configuration files were found.");
        GameRuntime.EnsureGameClosed(gameRoot);
        var original = new Dictionary<string, ConfigFileDocument>(StringComparer.OrdinalIgnoreCase);
        var edited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var picker = new ComboBox { ItemsSource = files.Select(path => Path.GetRelativePath(folderRoot, path)).ToList(), SelectedIndex = 0,
            MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Stretch };
        var editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 120, MaxHeight = 420,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(picker, "Configuration file");
        AutomationProperties.SetName(editor, "Configuration contents");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        string? selected = null;
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(picker); content.Children.Add(editor); content.Children.Add(status);
        var dialog = new ContentDialog { XamlRoot = xamlRoot, Title = "Edit configuration", Content = content,
            PrimaryButtonText = "Save changes", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };

        async Task LoadSelectedAsync()
        {
            if (selected is not null && editor.IsEnabled) edited[selected] = editor.Text;
            picker.IsEnabled = false;
            editor.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                selected = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, files[picker.SelectedIndex]));
                if (!original.ContainsKey(selected))
                {
                    if (new FileInfo(selected).Length > 4_194_304) throw new IOException("This configuration is too large for the built-in editor. Open its folder to edit it externally.");
                    original[selected] = ConfigFileDocument.Load(selected, await File.ReadAllBytesAsync(selected));
                    edited[selected] = original[selected].Text;
                }
                editor.Text = edited[selected];
                editor.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                status.Text = "Edits are kept when switching files. Save applies all changed files.";
            }
            catch (Exception ex) { editor.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false; status.Text = ex.Message; AppStorage.Log("Could not open configuration.", ex); }
            finally { picker.IsEnabled = true; }
        }
        await LoadSelectedAsync();
        picker.SelectionChanged += async (_, _) => await LoadSelectedAsync();
        if (await UiOperations.ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        if (selected is not null) edited[selected] = editor.Text;
        GameRuntime.EnsureGameClosed(gameRoot);
        var changes = edited
            .Select(item => new ConfigFileChange(original[item.Key], item.Value))
            .Where(change => !string.Equals(change.Text, change.Document.Text, StringComparison.Ordinal))
            .ToList();
        foreach (ConfigFileChange change in changes)
            _ = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, change.Document.Path));

        ConfigFileTransactions.Apply(changes);
        if (changes.Count > 0) AppStorage.Log("Saved configuration: " + string.Join(", ", changes.Select(change => Path.GetFileName(change.Document.Path))));
    }
}
