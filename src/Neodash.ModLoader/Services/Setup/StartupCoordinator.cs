namespace Neodash_Mod_Loader.Services;

public sealed class StartupCoordinator(ISettingsService settings, IGameService game, IRuntimeService runtime, IAppDiagnostics diagnostics)
{
    public AppPage GetInitialPage()
    {
        if (settings.LoadError is not null)
            return AppPage.Settings;
        if (!settings.IsSetupComplete)
        {
            string path = game.GetPath();
            if (path.Length > 0 && runtime.IsInstalled(path))
            {
                try
                {
                    settings.SetSetupComplete(true);
                }
                catch (Exception ex)
                {
                    diagnostics.Log("Could not save the existing UE4SS setup state; setup remains available.", ex);
                }
            }
        }

        return settings.IsSetupComplete ? AppPage.Shell : AppPage.Onboarding;
    }
}
