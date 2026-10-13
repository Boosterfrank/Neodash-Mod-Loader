using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public sealed partial class HomePageController(IModEditSession edits, IModWorkspace workspace, HomeProfileActions profileActions, HomeModActions modActions, IAppDiagnostics diagnostics, IUiDispatcher dispatcher, INavigationGuard navigation) : IDisposable
{
    private string? _loadError;
    private bool _active;
    public IReadOnlyList<EditableMod> Mods => edits.Mods;
    public bool HasUnsavedChanges => edits.HasUnsavedChanges;
    public bool IsBusy => workspace.IsBusy;
    public bool HasGame => modActions.HasGame;
    public string? LoadError => _loadError;
    public IReadOnlyList<ModProfile> Profiles => profileActions.Profiles;
    public ModProfile ActiveProfile => profileActions.ActiveProfile;

    public event EventHandler? Changed;
    public event EventHandler? Reloaded;
    public void Initialize()
    {
        _active = true;
        edits.Changed -= EditsChanged;
        edits.Changed += EditsChanged;
        workspace.Changed -= WorkspaceChanged;
        workspace.Changed += WorkspaceChanged;
        Reload();
    }

    public void Deactivate()
    {
        _active = false;
        edits.Changed -= EditsChanged;
        workspace.Changed -= WorkspaceChanged;
    }

    public void Reload()
    {
        _loadError = null;
        try
        {
            edits.Reload();
        }
        catch (Exception ex)
        {
            diagnostics.Log("Unable to load installed mods.", ex);
            _loadError = "Could not load installed mods: " + ex.Message + " Check the game folder in Settings, then Refresh.";
        }

        Reloaded?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> SaveAsync() => edits.SaveAsync();
    public void Discard() => edits.Discard();
    public Task LaunchAsync() => modActions.LaunchAsync();
    public Task OpenGameFolderAsync() => modActions.OpenGameFolderAsync();
    public async Task RefreshAsync()
    {
        if (await profileActions.ResolveUnsavedChangesAsync())
            Reload();
    }

    public async Task SelectProfileAsync(string name)
    {
        if (await profileActions.SelectAsync(name))
            Reload();
        else
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task CreateProfileAsync()
    {
        if (await profileActions.CreateAsync())
            Reload();
    }

    public async Task RenameProfileAsync()
    {
        if (await profileActions.RenameAsync())
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteProfileAsync()
    {
        if (await profileActions.DeleteAsync())
            Reload();
    }

    public Task ExportProfileAsync() => profileActions.ExportAsync();
    public async Task ImportProfileAsync()
    {
        if (await profileActions.ImportAsync())
            Reload();
    }

    public async Task ImportZipAsync()
    {
        if (await modActions.ImportZipAsync())
            Reload();
    }

    public async Task ImportFolderAsync()
    {
        if (await modActions.ImportFolderAsync())
            Reload();
    }

    public async Task ImportDroppedAsync(IReadOnlyList<DroppedMod> dropped)
    {
        if (await modActions.ImportDroppedAsync(dropped))
            Reload();
    }

    public Task OpenModFolderAsync(string name) => modActions.OpenModFolderAsync(name);
    public Task ReportDropFailureAsync(Exception exception) => modActions.ReportDropFailureAsync(exception);
    public async Task UninstallAsync(string name)
    {
        if (await modActions.UninstallAsync(name))
            Reload();
    }

    public async Task RepairAsync(string name)
    {
        if (await modActions.RepairAsync(name))
            Reload();
    }

    public async Task RestoreAsync(string name)
    {
        if (await modActions.RestoreAsync(name))
            Reload();
    }

    public bool HasModConfiguration(string name) => modActions.HasConfiguration(name);
    public Task EditConfigurationAsync(string name) => modActions.EditConfigurationAsync(name);
    public async Task ApplyAllAsync(IEnumerable<EditableMod> mods, bool enabled)
    {
        if (!await navigation.EnsureNotBusyAsync())
            return;
        foreach (EditableMod mod in mods)
            mod.IsEnabled = enabled;
        await edits.SaveAsync();
    }

    private void WorkspaceChanged(object? sender, EventArgs e) => dispatcher.Post(() =>
    {
        if (_active)
            Reload();
    });
    private void EditsChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() => Deactivate();
}
