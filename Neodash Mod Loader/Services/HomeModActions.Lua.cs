using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neodash_Mod_Loader.Services;

public sealed partial class HomeModActions
{
    public bool CanOpenInCode => files.CanOpenInCode;

    public bool HasLuaScripts(string name)
    {
        try { return workspace.GetLuaScriptFolder(name) is not null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public async Task OpenInCodeAsync(string name)
    {
        if (!await EnsureReadyAsync()) return;
        try
        {
            string folder = workspace.GetLuaScriptFolder(name) ?? throw new DirectoryNotFoundException("No Lua script was found for this mod. Refresh the mod list to check its files.");
            files.OpenInCode(folder);
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't open VS Code", ex.Message);
        }
    }

    public async Task<bool> CreateLuaAsync()
    {
        try
        {
            if (!await EnsureReadyAsync()) return false;
            var name = new TextBox
            {
                Header = "Mod name", PlaceholderText = "MyLuaMod", MaxLength = workspace.MaxLuaModNameLength,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var location = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            var openFolder = new CheckBox { Content = "Open mod folder after creating", IsChecked = true };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Create an enabled UE4SS Lua mod with a starter main.lua. Edit the script to add your mod's behavior.",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(name);
            content.Children.Add(location);
            content.Children.Add(openFolder);
            var dialog = new ContentDialog
            {
                Title = "Create Lua mod", Content = content, PrimaryButtonText = "Create mod",
                CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
            };
            void Validate()
            {
                string? error;
                try { error = workspace.ValidateLuaModName(name.Text); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = "Couldn't check the mod folders. Check access to the game folder, then try again."; }
                dialog.IsPrimaryButtonEnabled = error is null;
                name.Description = name.Text.Length == 0 ? null : error;
                location.Text = "Creates: Mods\\" + (error is null ? name.Text : "<mod name>") + "\\Scripts\\main.lua";
            }
            name.TextChanged += (_, _) => Validate();
            dialog.Opened += (_, _) => name.Focus(FocusState.Programmatic);
            Validate();
            if (await dialogs.ShowDialogAsync(dialog) != ContentDialogResult.Primary || !await ResolveBeforeActionAsync()) return false;
            if (!await RunAsync("Creating Lua mod", (p, t) => workspace.CreateLuaModAsync(name.Text, p, t))) return false;
            if (openFolder.IsChecked == true) await OpenModFolderAsync(name.Text);
            return true;
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't create Lua mod", ex.Message);
            return false;
        }
    }
}
