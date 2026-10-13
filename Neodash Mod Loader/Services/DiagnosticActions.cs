namespace Neodash_Mod_Loader.Services;

public sealed class DiagnosticActions(IAppDiagnostics diagnostics, IDesktopFiles files, IUserDialogs dialogs)
{
    public Version Version => diagnostics.Version;

    public void Open(string name) => files.OpenPath(diagnostics.GetLocation(name));
    public Task ShowSettingsErrorAsync(Exception exception)
    {
        diagnostics.Log("Settings operation failed.", exception);
        return dialogs.ShowMessageAsync("Could not complete action", exception.Message);
    }
}
