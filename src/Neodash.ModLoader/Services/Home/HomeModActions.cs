using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
/// <summary>Installed-mod commands, file pickers, and configuration handoff without page ownership.</summary>
public sealed partial class HomeModActions(IModWorkspace workspace, IGameService game, INavigationGuard navigation, IDesktopDialogs dialogs, IDesktopFiles files, IConfigurationService configurations, IConfigurationEditor editor)
{
    public bool HasGame => game.GetPath().Length > 0;

    public async Task LaunchAsync()
    {
        if (!await EnsureReadyAsync())
            return;
        if (!await navigation.ResolveUnsavedChangesAsync("Save changes before launching Neodash?"))
            return;
        try
        {
            game.Launch();
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't launch Neodash", ex.Message);
        }
    }

    public async Task OpenGameFolderAsync()
    {
        try
        {
            files.OpenPath(game.RequirePath());
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't open the game folder", ex.Message);
        }
    }

    public async Task<bool> ImportZipAsync()
    {
        try
        {
            if (await files.PickFileAsync(".zip")is not { } path || !await ResolveBeforeActionAsync())
                return false;
            return await RunAsync("Installing mod", (p, t) => workspace.ImportZipAsync(path, p, t));
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't install mod", ex.Message);
            return false;
        }
    }

    public async Task<bool> ImportFolderAsync()
    {
        try
        {
            if (await files.PickFolderAsync()is not { } path || !await ResolveBeforeActionAsync())
                return false;
            return await RunAsync("Importing mod folder", (p, t) => workspace.ImportFolderAsync(path, p, t));
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't import mod folder", ex.Message);
            return false;
        }
    }

    public async Task<bool> ImportDroppedAsync(IReadOnlyList<DroppedMod> dropped)
    {
        try
        {
            if (!await EnsureReadyAsync() || dropped.Count == 0)
                return false;
            if (dropped.Any(item => !item.IsFolder && !Path.GetExtension(item.SourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Drop mod ZIP files or mod folders. Other formats are not supported.");
            if (!await ResolveBeforeActionAsync())
                return false;
            return await RunAsync("Installing dropped mods", async (p, t) =>
            {
                for (int index = 0; index < dropped.Count; index++)
                {
                    t.ThrowIfCancellationRequested();
                    var item = dropped[index];
                    var progress = new ItemOperationProgress(p, Path.GetFileName(Path.TrimEndingDirectorySeparator(item.SourcePath)), index, dropped.Count);
                    progress.Report(new("Starting…"));
                    if (item.IsFolder)
                        await workspace.ImportFolderAsync(item.SourcePath, progress, t);
                    else
                        await workspace.ImportZipAsync(item.SourcePath, progress, t);
                    progress.Report(new("Installed", 100));
                }
            });
        }
        catch (Exception ex)
        {
            await ReportDropFailureAsync(ex);
            return false;
        }
    }

    public Task ReportDropFailureAsync(Exception exception) => dialogs.ShowMessageAsync("Couldn't import dropped mods", exception.Message);
    public async Task OpenModFolderAsync(string name)
    {
        try
        {
            files.OpenPath(workspace.GetModFolder(name));
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't open mod folder", ex.Message);
        }
    }

    public async Task<bool> UninstallAsync(string name)
    {
        if (!await ResolveBeforeActionAsync())
            return false;
        IReadOnlyList<string> folders = workspace.GetOwnedFolders(name);
        string detail = folders.Count > 1 ? $" This package owns {folders.Count} folders, and all will be deleted." : "";
        return await dialogs.ConfirmAsync("Uninstall mod", $"Delete {string.Join(", ", folders)} from Neodash? This cannot be undone.{detail}", "Uninstall") && await RunAsync("Uninstalling mod", (_, _) => workspace.UninstallAsync(name), false);
    }

    public async Task<bool> RepairAsync(string name) => await ResolveBeforeActionAsync() && await RunAsync("Repairing mod", (p, t) => workspace.RepairAsync(name, p, t));
    public async Task<bool> RestoreAsync(string name) => await ResolveBeforeActionAsync() && await RunAsync("Restoring backup", (_, _) => workspace.RestoreAsync(name), false);
    public bool HasConfiguration(string name)
    {
        try
        {
            return configurations.GetModConfiguration(name).Files.Count > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task EditConfigurationAsync(string name)
    {
        if (!await EnsureReadyAsync() || !await ResolveBeforeActionAsync())
            return;
        try
        {
            if (!HasConfiguration(name))
            {
                await dialogs.ShowMessageAsync("No configuration found", "This mod has no .ini, .cfg, .toml, or .json files.");
                return;
            }

            await editor.EditModAsync(name);
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Couldn't edit configuration", ex.Message);
        }
    }

    private Task<bool> ResolveBeforeActionAsync() => navigation.ResolveUnsavedChangesAsync("Save changes before continuing?");
    private Task<bool> EnsureReadyAsync() => navigation.EnsureNotBusyAsync();
    private async Task<bool> RunAsync(string title, Func<IProgress<OperationProgress>, CancellationToken, Task> action, bool cancellation = true) => await dialogs.RunAsync(title, action, cancellation);
}
