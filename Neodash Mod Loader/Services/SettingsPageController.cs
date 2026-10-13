namespace Neodash_Mod_Loader.Services;

public sealed record SettingsPageState(string GamePath, string RuntimeStatus, bool HasGame, bool HasRuntimeConfiguration, string? LoadError, Version Version);
public sealed class SettingsPageController(PreferencesController preferences, GameFolderPicker folders, RuntimeActions runtime, DiagnosticActions diagnostics, AppUpdateCoordinator updates)
{
    public string? Theme => preferences.Theme;
    public bool IsBusy => preferences.IsBusy;

    public SettingsPageState GetState()
    {
        string path = preferences.GamePath;
        return new(path, runtime.GetStatus(path), path.Length > 0, path.Length > 0 && runtime.HasConfiguration, preferences.LoadError, diagnostics.Version);
    }

    public void SetTheme(string theme) => preferences.SetTheme(theme);
    public void RetryLoading() => preferences.RetryLoading();
    public Task<bool> ChooseFolderAsync() => folders.ChooseAsync(ensureGameClosed: true);
    public void DetectGame() => preferences.DetectGame();
    public Task InstallRuntimeAsync() => runtime.InstallAsync();
    public Task RemoveRuntimeAsync() => runtime.RemoveAsync();
    public void OpenLocation(string name) => diagnostics.Open(name);
    public Task EditRuntimeAsync() => runtime.EditAsync();
    public Task CheckUpdatesAsync() => updates.CheckAsync(showUpToDate: true);
    public Task RestartSetupAsync() => preferences.RestartSetupAsync();
    public Task ShowErrorAsync(Exception exception) => diagnostics.ShowSettingsErrorAsync(exception);
}
