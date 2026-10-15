using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public sealed class ModLibraryController(IModCatalog catalog, IModWorkspace workspace, IDesktopDialogs dialogs, IDesktopFiles files, IAppDiagnostics diagnostics)
{
    public bool IsBusy => workspace.IsBusy;

    public event EventHandler? Changed { add => workspace.Changed += value; remove => workspace.Changed -= value; }

    public Task<ModLibraryResponse> LoadAsync() => catalog.LoadAsync();
    public CatalogModState GetState(ModLibraryItem mod) => catalog.GetState(mod);
    public void LogLoadFailure(Exception exception) => diagnostics.Log("Library load failed.", exception);
    public async Task InstallAsync(ModLibraryItem mod)
    {
        if (IsBusy)
            return;
        string? version = catalog.GetState(mod).InstalledVersion;
        if (ModVersion.Compare(mod.Version, version) < 0 && !await dialogs.ConfirmAsync("Install older version?", $"The library offers {mod.Version}, but {version} is already installed. Replace it with the older version?", "Install older version"))
            return;
        await dialogs.RunAsync("Install " + mod.Name, (p, t) => catalog.InstallAsync(mod, p, t));
    }

    public async Task UninstallAsync(ModLibraryItem mod)
    {
        if (IsBusy)
            return;
        var folders = catalog.GetUninstallFolders(mod);
        if (folders.Count == 0 || !await dialogs.ConfirmAsync("Delete mod?", $"Permanently delete {string.Join(", ", folders)} and remove it from profiles for this game installation?", "Uninstall"))
            return;
        await dialogs.RunAsync("Uninstall " + mod.Name, (_, _) => catalog.UninstallAsync(mod), allowCancellation: false);
    }

    public async Task UpdateAllAsync(IReadOnlyList<ModLibraryItem> mods)
    {
        if (IsBusy)
            return;
        var updates = mods.Where(mod => GetState(mod).HasUpdate).ToList();
        if (updates.Count == 0)
            return;
        await dialogs.RunAsync("Update mods", async (progress, token) =>
        {
            for (int index = 0; index < updates.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var mod = updates[index];
                var itemProgress = new ItemOperationProgress(progress, mod.Name, index, updates.Count);
                itemProgress.Report(new("Starting…"));
                await catalog.InstallAsync(mod, itemProgress, token);
                itemProgress.Report(new("Installed", 100));
            }
        });
    }

    public async Task OpenFolderAsync(ModLibraryItem mod)
    {
        try
        {
            var folders = catalog.GetFolders(mod);
            if (folders.Count == 0)
                throw new DirectoryNotFoundException("The mod folder no longer exists. Refresh the library to update its status.");
            ModFolderLocation selected = folders[0];
            if (folders.Count > 1)
            {
                var picker = new ComboBox
                {
                    Header = "Mod folder",
                    ItemsSource = folders.Select(folder => folder.Name).ToList(),
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var dialog = new ContentDialog
                {
                    Title = "Open " + mod.Name + " folder",
                    Content = picker,
                    PrimaryButtonText = "Open folder",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };
                dialog.Opened += (_, _) => picker.Focus(FocusState.Programmatic);
                if (await dialogs.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
                    return;
                selected = folders[picker.SelectedIndex];
            }

            files.OpenPath(selected.Path);
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't open mod folder", ex.Message);
        }
    }
}
