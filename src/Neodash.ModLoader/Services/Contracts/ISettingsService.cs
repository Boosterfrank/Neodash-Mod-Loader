using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface ISettingsService
{
    string? Theme { get; }

    bool IsSetupComplete { get; }

    string? LoadError { get; }

    void Reload();
    void SetTheme(string theme);
    void SetSetupComplete(bool complete);
    void ChooseGameFolder(string selectedFolder, bool ensureGameClosed);
    void DetectGame();
}
