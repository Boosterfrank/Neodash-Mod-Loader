namespace Neodash_Mod_Loader.Services;
public sealed class SettingsPageController(PreferencesController preferences, GameFolderPicker folders, RuntimeActions runtime, DiagnosticActions diagnostics, AppUpdateCoordinator updates)
{
    public string? Theme => preferences.Theme;
    public bool IsBusy => preferences.IsBusy;

    public SettingsPageState GetState()
    {
        string path = preferences.GamePath;
        return new(path, runtime.GetStatus(path), path.Length > 0, path.Length > 0 && runtime.HasConfiguration, runtime.CanRemove(path), diagnostics.CanOpen("ue4ss"), diagnostics.CanOpen("operations"), diagnostics.CanOpen("crash"), preferences.LoadError, diagnostics.Version, diagnostics.CommitHash);
    }

    public void SetTheme(string theme) => preferences.SetTheme(theme);
    public void RetryLoading() => preferences.RetryLoading();
    public Task<bool> ChooseFolderAsync() => folders.ChooseAsync(ensureGameClosed: true);
    public void DetectGame() => preferences.DetectGame();
    public Task InstallRuntimeAsync() => runtime.InstallAsync();
    public Task RemoveRuntimeAsync() => runtime.RemoveAsync();
    public Task OpenLocationAsync(string name) => diagnostics.OpenAsync(name);
    public bool CopyCommit() => diagnostics.CopyCommit();
    public Task EditRuntimeAsync() => runtime.EditAsync();
    public Task CheckUpdatesAsync() => updates.CheckAsync(showUpToDate: true);
    public Task RestartSetupAsync() => preferences.RestartSetupAsync();
    public Task ShowErrorAsync(Exception exception) => diagnostics.ShowSettingsErrorAsync(exception);
}
