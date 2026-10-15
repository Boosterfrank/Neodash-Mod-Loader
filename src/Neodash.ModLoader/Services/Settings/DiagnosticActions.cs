namespace Neodash_Mod_Loader.Services;

public sealed class DiagnosticActions(IAppDiagnostics diagnostics, IDesktopFiles files, IUserDialogs dialogs, IDesktopClipboard clipboard)
{
    public Version Version => diagnostics.Version;
    public string? CommitHash => diagnostics.CommitHash;

    public bool CopyCommit()
    {
        string? hash = CommitHash;
        if (string.IsNullOrWhiteSpace(hash)) return false;
        clipboard.SetText(hash);
        return true;
    }

    public async Task OpenAsync(string name)
    {
        bool isLog = name is "crash" or "operations" or "ue4ss";
        if (isLog && !CanOpen(name))
        {
            await ShowUnavailableLogAsync(name);
            return;
        }

        try
        {
            files.OpenPath(diagnostics.GetLocation(name));
        }
        catch (Exception exception) when (isLog && exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // The log can disappear between the availability check and the shell-open request.
            await ShowUnavailableLogAsync(name);
        }
    }

    private Task ShowUnavailableLogAsync(string name)
    {
        string label = name switch
        {
            "crash" => "Crash log",
            "operations" => "Operation log",
            _ => "UE4SS log"
        };
        return dialogs.ShowMessageAsync("Log unavailable", $"{label} has not been created or is no longer present.");
    }

    public bool CanOpen(string name) => diagnostics.CanOpenLocation(name);
    public Task ShowSettingsErrorAsync(Exception exception)
    {
        diagnostics.Log("Settings operation failed.", exception);
        return dialogs.ShowMessageAsync("Could not complete action", exception.Message);
    }
}
