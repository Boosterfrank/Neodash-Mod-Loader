using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public sealed class HomeProfileActions(IProfileService profiles, INavigationGuard navigation, IDesktopDialogs dialogs, IDesktopFiles files, IUserNotifications notifications)
{
    public IReadOnlyList<ModProfile> Profiles => profiles.Profiles;
    public ModProfile ActiveProfile => profiles.ActiveProfile;

    public Task<bool> ResolveUnsavedChangesAsync() => navigation.ResolveUnsavedChangesAsync($"Save changes to \"{profiles.ActiveProfile.Name}\" before continuing?");
    public async Task<bool> SelectAsync(string name)
    {
        if (name == profiles.ActiveProfile.Name || !await ResolveUnsavedChangesAsync())
            return false;
        ModProfile? target = profiles.Profiles.FirstOrDefault(profile => profile.Name == name);
        return target is not null && await SwitchAsync(target);
    }

    public async Task<bool> CreateAsync()
    {
        if (!await ResolveUnsavedChangesAsync())
            return false;
        string? name = await PromptAsync("New Profile", "Create", "", null, "The new profile starts with the mods enabled right now.");
        if (name is null)
            return false;
        try
        {
            profiles.Create(name);
            return true;
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't create profile", ex.Message);
            return false;
        }
    }

    public async Task<bool> RenameAsync()
    {
        if (!await ResolveUnsavedChangesAsync())
            return false;
        ModProfile profile = profiles.ActiveProfile;
        string? name = await PromptAsync("Rename Profile", "Rename", profile.Name, profile, null);
        if (name is null)
            return false;
        try
        {
            profiles.Rename(profile, name);
            return true;
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't rename profile", ex.Message);
            return false;
        }
    }

    public async Task<bool> DeleteAsync()
    {
        if (!await ResolveUnsavedChangesAsync())
            return false;
        ModProfile profile = profiles.ActiveProfile;
        ModProfile? fallback = profiles.Profiles.FirstOrDefault(item => item != profile);
        if (fallback is null || !await dialogs.ConfirmAsync("Delete Profile", $"Delete \"{profile.Name}\" and switch to \"{fallback.Name}\"? Installed mods are kept.", "Delete"))
            return false;
        if (!await SwitchAsync(fallback))
            return false;
        try
        {
            profiles.Delete(profile);
            return true;
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't delete profile", ex.Message);
            // Switching to the fallback already changed the active profile and installed mod state.
            return true;
        }
    }

    public async Task ExportAsync()
    {
        try
        {
            if (!await ResolveUnsavedChangesAsync())
                return;
            string? path = await files.SaveFileAsync(profiles.ActiveProfile.Name + ".neodash-profile", ".json");
            if (path is not null)
            {
                profiles.Export(path);
                notifications.Show("Profile exported", Path.GetFileName(path));
            }
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't export profile", ex.Message);
        }
    }

    public async Task<bool> ImportAsync()
    {
        try
        {
            if (!await ResolveUnsavedChangesAsync())
                return false;
            string? path = await files.PickFileAsync(".json");
            if (path is null)
                return false;
            ModProfile profile = profiles.Import(path);
            if (await dialogs.ConfirmAsync("Profile imported", $"Switch to imported profile \"{profile.Name}\" now?", "Switch"))
                await SwitchAsync(profile);
            // The imported profile remains available even when switching was cancelled or failed.
            return true;
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't import profile", ex.Message);
            return false;
        }
    }

    private async Task<bool> SwitchAsync(ModProfile target)
    {
        if (!await navigation.EnsureNotBusyAsync())
            return false;
        ProfileSwitchPlan plan;
        try
        {
            plan = profiles.PrepareSwitch(target);
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Can't switch profiles", ex.Message);
            return false;
        }

        if (plan.MissingMods.Count > 0)
        {
            await dialogs.ShowMessageAsync("Profile has missing mods", "Those mods are not installed and will stay disabled.\n\n" + string.Join("\n", plan.MissingMods.Select(name => "• " + name)));
        }

        ProfileSwitchResult result;
        try
        {
            result = profiles.CommitSwitch(plan);
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Can't switch profiles", ex.Message);
            return false;
        }
        if (result.Success)
            return true;
        string message = result.Message ?? "";
        if (result.Errors is { Count: > 0 })
            message += "\n\n" + string.Join("\n", result.Errors.Select(error => "• " + error));
        await dialogs.ShowMessageAsync(result.Title ?? "Couldn't switch profiles", message);
        return false;
    }

    private async Task<string?> PromptAsync(string title, string primary, string initial, ModProfile? renaming, string? description)
    {
        var name = new TextBox
        {
            Text = initial,
            PlaceholderText = "Profile name",
            MaxLength = profiles.MaxNameLength
        };
        var content = new StackPanel
        {
            Spacing = 12
        };
        if (description is not null)
            content.Children.Add(new TextBlock { Text = description, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        content.Children.Add(name);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        void Validate()
        {
            string? error = profiles.ValidateName(name.Text, renaming);
            dialog.IsPrimaryButtonEnabled = error is null;
            name.Description = string.IsNullOrWhiteSpace(name.Text) ? null : error;
        }

        name.TextChanged += (_, _) => Validate();
        dialog.Opened += (_, _) => name.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        Validate();
        return await dialogs.ShowDialogAsync(dialog) == ContentDialogResult.Primary ? name.Text.Trim() : null;
    }
}
