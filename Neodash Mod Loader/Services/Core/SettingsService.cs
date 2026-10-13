using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Keeps settings mutations recoverable while delegating validation and persistence to SettingsManager.</summary>
public sealed class SettingsService(IGameService game) : ISettingsService
{
    public string? Theme => SettingsManager.Current.AppTheme;
    public bool IsSetupComplete => SettingsManager.Current.IsSetupComplete;
    public string? LoadError => SettingsManager.LoadError;

    public void Reload() => SettingsManager.LoadSettings();
    public void SetTheme(string theme)
    {
        string? previous = SettingsManager.Current.AppTheme;
        SettingsManager.Current.AppTheme = theme;
        try
        {
            SettingsManager.SaveSettings();
        }
        catch
        {
            SettingsManager.Current.AppTheme = previous;
            throw;
        }
    }

    public void SetSetupComplete(bool complete)
    {
        bool previous = SettingsManager.Current.IsSetupComplete;
        SettingsManager.Current.IsSetupComplete = complete;
        try
        {
            SettingsManager.SaveSettings();
        }
        catch
        {
            SettingsManager.Current.IsSetupComplete = previous;
            throw;
        }
    }

    public void ChooseGameFolder(string selectedFolder, bool ensureGameClosed)
    {
        string path = GamePathResolver.NormalizeGameDirectory(selectedFolder) ?? throw new IOException("Choose the Neodash folder containing Runway-Win64-Shipping.exe, or its game installation root.");
        if (ensureGameClosed)
            game.EnsureClosed(path);
        var previous = SettingsManager.CaptureSnapshot();
        SettingsManager.Current.GameDirectory = path;
        SettingsManager.Current.ModsTxtMigrated = false;
        try
        {
            SettingsManager.SaveSettings();
        }
        catch
        {
            SettingsManager.RestoreSnapshot(previous, persist: false);
            throw;
        }
    }

    public void DetectGame()
    {
        var previous = SettingsManager.CaptureSnapshot();
        try
        {
            SettingsManager.Current.GameDirectory = null;
            if (game.GetPath().Length == 0)
                throw new DirectoryNotFoundException("Neodash was not found in Steam libraries. Choose its folder manually.");
            SettingsManager.Current.ModsTxtMigrated = false;
            SettingsManager.SaveSettings();
        }
        catch
        {
            SettingsManager.RestoreSnapshot(previous, persist: false);
            throw;
        }
    }
}
