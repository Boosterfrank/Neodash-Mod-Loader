namespace Neodash_Mod_Loader;

/// <summary>Changes installed mod states and persists the active profile as one recoverable operation.</summary>
public static class ModStateTransactions
{
    public static void ApplyChanges(string gameRoot, IReadOnlyDictionary<string, bool> changes)
    {
        ProfileManager.EnsureWritable();
        ProfileManager.EnsureProfileGamePath(ProfileManager.ActiveProfile, gameRoot);
        GameRuntime.EnsureGameClosed(gameRoot);
        var installed = ModManager.GetMods(gameRoot).ToDictionary(mod => mod.Name, mod => mod.IsEnabled, StringComparer.OrdinalIgnoreCase);
        foreach (string name in changes.Keys)
        {
            if (!installed.ContainsKey(name)) throw new DirectoryNotFoundException("The mod folder no longer exists: " + name);
            foreach (bool enabled in new[] { true, false })
                _ = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, ModManager.GetModPath(gameRoot, name, enabled)));
        }
        string modsTxt = SafePaths.Child(gameRoot, Path.Combine(ModManager.ModsFolderName, "mods.txt"));
        byte[]? originalModsTxt = File.Exists(modsTxt) ? File.ReadAllBytes(modsTxt) : null;
        List<string> moved = [];
        try
        {
            foreach (var (name, enabled) in changes)
            {
                if (installed[name] == enabled) continue;
                ModManager.SetModEnabled(gameRoot, name, enabled);
                moved.Add(name);
            }
            ProfileManager.SyncActiveProfile(gameRoot);
        }
        catch (Exception failure)
        {
            RestorePreviousState(gameRoot, installed, moved, modsTxt, originalModsTxt, failure);
            throw;
        }
    }

    private static void RestorePreviousState(string gameRoot, Dictionary<string, bool> installed,
        List<string> moved, string modsTxt, byte[]? originalModsTxt, Exception failure)
    {
        List<Exception> errors = [failure];
        foreach (string name in moved.AsEnumerable().Reverse())
        {
            try { ModManager.SetModEnabled(gameRoot, name, installed[name]); }
            catch (Exception rollback) { errors.Add(rollback); }
        }
        try
        {
            if (originalModsTxt is not null && (!File.Exists(modsTxt) || !File.ReadAllBytes(modsTxt).AsSpan().SequenceEqual(originalModsTxt)))
                AppStorage.WriteAtomically(modsTxt, originalModsTxt);
        }
        catch (Exception rollback) { errors.Add(rollback); }
        if (errors.Count > 1) throw new AggregateException("Mod changes could not be saved, and some previous states could not be restored.", errors);
    }
}
