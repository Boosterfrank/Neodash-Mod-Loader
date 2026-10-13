namespace Neodash_Mod_Loader.Services;

/// <summary>Shares folder selection between Settings and first-run setup.</summary>
public sealed class GameFolderPicker(ISettingsService settings, IDesktopFiles files)
{
    public async Task<bool> ChooseAsync(bool ensureGameClosed)
    {
        string? selected = await files.PickFolderAsync();
        if (selected is null)
            return false;
        settings.ChooseGameFolder(selected, ensureGameClosed);
        return true;
    }
}
