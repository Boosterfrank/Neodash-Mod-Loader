namespace Neodash_Mod_Loader;

/// <summary>Upgrades legacy mod state without marking incomplete migrations as complete.</summary>
public static class SettingsMigration
{
    public static bool MigrateLegacyModState(string win64Path)
    {
        if (SettingsManager.Current.ModsTxtMigrated) return true;
        SettingsManager.EnsureWritable();
        ProfileManager.EnsureWritable();
        ProfileManager.EnsureProfileGamePath(ProfileManager.ActiveProfile, win64Path);
        GameRuntime.EnsureGameClosed(win64Path);

        if (!ModManager.MigrateModsTxtDisabledMods(win64Path)) return false;

        // A legacy profile may still select a mod disabled through mods.txt. Persist the reconciled
        // profile first, so switching profiles cannot undo that old choice and a failed save can retry.
        ProfileManager.SyncActiveProfile(win64Path);
        SettingsManager.Current.ModsTxtMigrated = true;
        try { SettingsManager.SaveSettings(); }
        catch
        {
            SettingsManager.Current.ModsTxtMigrated = false;
            throw;
        }
        return true;
    }
}
