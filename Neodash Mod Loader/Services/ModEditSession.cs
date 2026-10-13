using System.ComponentModel;

namespace Neodash_Mod_Loader.Services;

/// <summary>Owns a window's pending mod selections independently of its current page.</summary>
public sealed partial class ModEditSession(IModWorkspace workspace, IGameService game, IUserDialogs dialogs, IAppDiagnostics diagnostics) : IModEditSession, IDisposable
{
    private readonly List<EditableMod> _mods = [];
    public IReadOnlyList<EditableMod> Mods => _mods;
    public bool HasUnsavedChanges => _mods.Any(mod => mod.IsEnabled != mod.OriginalIsEnabled);

    public event EventHandler? Changed;
    public void Reload()
    {
        Clear();
        try
        {
            foreach (var state in workspace.GetInstalledMods())
            {
                var mod = new EditableMod(state);
                mod.PropertyChanged += Mod_PropertyChanged;
                _mods.Add(mod);
            }
        }
        catch
        {
            Clear();
            throw;
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Discard()
    {
        foreach (var mod in _mods)
            mod.IsEnabled = mod.OriginalIsEnabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> SaveAsync()
    {
        if (!HasUnsavedChanges)
            return true;
        if (workspace.IsBusy)
        {
            await dialogs.ShowMessageAsync(ModOperationMessages.BusyTitle, ModOperationMessages.BusyMessage);
            return false;
        }

        try
        {
            game.EnsureClosed();
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Close Neodash first", ex.Message);
            return false;
        }

        try
        {
            workspace.ValidateEdits();
        }
        catch (Exception ex)
        {
            await dialogs.ShowMessageAsync("Can't save mod changes", ex.Message);
            return false;
        }

        var changed = _mods.Where(mod => mod.IsEnabled != mod.OriginalIsEnabled).ToList();
        try
        {
            workspace.ApplyChanges(changed.ToDictionary(mod => mod.Name, mod => mod.IsEnabled, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            diagnostics.Log("Could not save mod states and profile.", ex);
            await dialogs.ShowMessageAsync("Couldn't save mod changes", ex.Message);
            return false;
        }

        foreach (var mod in changed)
        {
            mod.OriginalIsEnabled = mod.IsEnabled;
            try
            {
                mod.EntryPointPath = workspace.GetEntryPoint(mod.Name, mod.IsEnabled);
            }
            catch (Exception ex)
            {
                mod.EntryPointPath = string.Empty;
                diagnostics.Log("Could not read a mod's entry point.", ex);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Mod_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    private void Clear()
    {
        foreach (var mod in _mods)
            mod.PropertyChanged -= Mod_PropertyChanged;
        _mods.Clear();
    }

    public void Dispose() => Clear();
}
