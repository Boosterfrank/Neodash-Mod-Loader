namespace Neodash_Mod_Loader.Services;

public sealed class PreferencesController(ISettingsService settings, IGameService game, IModWorkspace workspace, IUserDialogs dialogs, IAppNavigator navigator, IWindowContext window)
{
    public string? Theme => settings.Theme;
    public string? LoadError => settings.LoadError;
    public string GamePath => game.GetPath();
    public bool IsBusy => workspace.IsBusy;

    public void SetTheme(string theme)
    {
        settings.SetTheme(theme);
        window.ApplyTheme(theme);
    }

    public void RetryLoading()
    {
        settings.Reload();
        if (settings.LoadError is not null)
            return;
        window.ApplyTheme(settings.Theme);
        navigator.NavigateRoot(settings.IsSetupComplete ? AppPage.Shell : AppPage.Onboarding);
    }

    public void DetectGame() => settings.DetectGame();
    public void CompleteSetup()
    {
        settings.SetSetupComplete(true);
        navigator.NavigateRoot(AppPage.Shell);
    }

    public async Task RestartSetupAsync()
    {
        if (!await dialogs.ConfirmAsync("Run setup again?", "Show setup again? Existing mods, profiles, and settings will be kept.", "Show setup"))
            return;
        settings.SetSetupComplete(false);
        navigator.NavigateRoot(AppPage.Onboarding);
    }
}
