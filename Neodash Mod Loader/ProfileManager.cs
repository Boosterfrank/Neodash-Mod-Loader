using Neodash_Mod_Loader.Models;
using System.Text.Json;

namespace Neodash_Mod_Loader;

/// <summary>Stores named enabled-mod sets. Profile files never include mod contents or configuration files.</summary>
public static class ProfileManager
{
    public const int MaxNameLength = 40;
    private const string DefaultProfileName = "Default";
    private static readonly string ProfilesDirectory = AppStorage.DirectoryPath;
    private static readonly string ProfilesPath = Path.Combine(ProfilesDirectory, "profiles.json");
    private static string? _loadError;
    private static readonly ModProfileData Data = LoadProfiles();

    public static IReadOnlyList<ModProfile> Profiles => Data.Profiles;
    public static ModProfile ActiveProfile => Data.Profiles.First(profile => profile.Name == Data.ActiveProfile);

    private static ModProfileData LoadProfiles()
    {
        ModProfileData? data = null;
        try
        {
            if (File.Exists(ProfilesPath))
            {
                data = ProfileJsonFormat.Deserialize(File.ReadAllText(ProfilesPath));
                ValidateLoadedData(data);
            }
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            AppStorage.Log("profiles.json could not be read; it will not be overwritten.", ex);
            data = null;
        }

        data ??= new ModProfileData();
        data.Profiles ??= [];
        foreach (ModProfile profile in data.Profiles)
        {
            profile.Name = profile.Name.Trim();
            profile.EnabledMods = NormalizeMods(profile.EnabledMods);
            if (profile.GamePath is not null) profile.GamePath = NormalizeGamePath(profile.GamePath);
        }
        if (data.Profiles.Count == 0) data.Profiles.Add(new ModProfile { Name = DefaultProfileName });
        data.ActiveProfile = data.Profiles.FirstOrDefault(profile => string.Equals(profile.Name, data.ActiveProfile, StringComparison.OrdinalIgnoreCase))?.Name
            ?? data.Profiles[0].Name;
        return data;
    }

    private static void ValidateLoadedData(ModProfileData data)
    {
        data.Profiles ??= [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModProfile? profile in data.Profiles)
        {
            if (profile is null) throw new JsonException("profiles.json contains an invalid profile.");
            string? name = profile.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || !names.Add(name))
                throw new JsonException("profiles.json contains an invalid or duplicate profile name.");
            if (profile.GamePath is not null && (!Path.IsPathFullyQualified(profile.GamePath) || string.IsNullOrWhiteSpace(profile.GamePath)))
                throw new JsonException("profiles.json contains an invalid bound game path.");
            if (profile.GamePath is not null) _ = NormalizeGamePath(profile.GamePath);
        }
        if (data.ActiveProfile is not null && !names.Contains(data.ActiveProfile))
            throw new JsonException("profiles.json names an active profile that does not exist.");
    }

    /// <summary>Fails before mod filesystem work when profiles.json is unreadable, preserving the original recovery file.</summary>
    public static void EnsureWritable()
    {
        if (_loadError is not null)
            throw new InvalidOperationException($"Profiles could not be loaded. The file was preserved at '{ProfilesPath}': {_loadError}");
    }

    private static List<string> NormalizeMods(IEnumerable<string>? names) => names?
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList() ?? [];

    private static string NormalizeGamePath(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) throw new ArgumentException("Choose a Neodash Win64 folder first.", nameof(gamePath));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
    }

    private static bool SameGame(string first, string second) => string.Equals(NormalizeGamePath(first), NormalizeGamePath(second), StringComparison.OrdinalIgnoreCase);

    private static void SaveProfiles()
    {
        EnsureWritable();
        ValidateLoadedData(Data);
        string content = ProfileJsonFormat.Serialize(Data);
        WriteAtomically(ProfilesPath, content);
    }

    private static void WriteAtomically(string path, string content)
    {
        AppStorage.WriteAtomically(path, content);
    }

    public static string? ValidateName(string? name, ModProfile? renaming = null) => ValidateName(name, Data.Profiles, renaming);

    private static string? ValidateName(string? name, IEnumerable<ModProfile> profiles, ModProfile? renaming)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return "Enter a name.";
        if (name.Length > MaxNameLength) return $"Use {MaxNameLength} characters or fewer.";
        return profiles.Any(profile => profile != renaming && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            ? "A profile with that name already exists."
            : null;
    }

    public static ModProfile CreateProfile(string name)
    {
        EnsureWritable();
        string? error = ValidateName(name);
        if (error is not null) throw new ArgumentException(error, nameof(name));
        var profile = new ModProfile { Name = name.Trim(), EnabledMods = [.. ActiveProfile.EnabledMods], GamePath = ActiveProfile.GamePath };
        string previousActive = Data.ActiveProfile ?? DefaultProfileName;
        Data.Profiles.Add(profile);
        Data.ActiveProfile = profile.Name;
        try { SaveProfiles(); }
        catch { Data.Profiles.Remove(profile); Data.ActiveProfile = previousActive; throw; }
        return profile;
    }

    /// <summary>Creates and selects a profile bound to this game, using the folders enabled on disk now.</summary>
    public static ModProfile CreateProfile(string name, string win64Path)
    {
        EnsureWritable();
        string? error = ValidateName(name);
        if (error is not null) throw new ArgumentException(error, nameof(name));
        string normalizedPath = NormalizeGamePath(win64Path);
        var enabled = ModManager.GetMods(normalizedPath).Where(mod => mod.IsEnabled).Select(mod => mod.Name);
        var profile = new ModProfile { Name = name.Trim(), EnabledMods = NormalizeMods(enabled), GamePath = normalizedPath };
        string previousActive = Data.ActiveProfile ?? DefaultProfileName;
        Data.Profiles.Add(profile);
        Data.ActiveProfile = profile.Name;
        try { SaveProfiles(); }
        catch { Data.Profiles.Remove(profile); Data.ActiveProfile = previousActive; throw; }
        return profile;
    }

    public static void RenameProfile(ModProfile profile, string newName)
    {
        EnsureWritable();
        string? error = ValidateName(newName, profile);
        if (error is not null) throw new ArgumentException(error, nameof(newName));
        bool active = profile == ActiveProfile;
        string previousName = profile.Name;
        string? previousActive = Data.ActiveProfile;
        profile.Name = newName.Trim();
        if (active) Data.ActiveProfile = profile.Name;
        try { SaveProfiles(); }
        catch { profile.Name = previousName; Data.ActiveProfile = previousActive; throw; }
    }

    public static void DeleteProfile(ModProfile profile)
    {
        EnsureWritable();
        if (profile == ActiveProfile) throw new InvalidOperationException("Can't delete the active profile.");
        int index = Data.Profiles.IndexOf(profile);
        Data.Profiles.Remove(profile);
        try { SaveProfiles(); }
        catch { Data.Profiles.Insert(index, profile); throw; }
    }

    public static void SetActiveProfile(ModProfile profile)
    {
        EnsureWritable();
        if (!Data.Profiles.Contains(profile)) throw new ArgumentException("The profile is not managed by this loader.", nameof(profile));
        string? previousActive = Data.ActiveProfile;
        Data.ActiveProfile = profile.Name;
        try { SaveProfiles(); }
        catch { Data.ActiveProfile = previousActive; throw; }
    }

    /// <summary>Validates that a profile belongs to this game, then binds a legacy profile and selects it atomically.</summary>
    public static void SetActiveProfile(ModProfile profile, string win64Path)
    {
        EnsureWritable();
        if (!Data.Profiles.Contains(profile)) throw new ArgumentException("The profile is not managed by this loader.", nameof(profile));
        EnsureProfileGamePath(profile, win64Path);
        string normalizedPath = NormalizeGamePath(win64Path);
        string? previousActive = Data.ActiveProfile;
        string? previousPath = profile.GamePath;
        Data.ActiveProfile = profile.Name;
        profile.GamePath ??= normalizedPath;
        try { SaveProfiles(); }
        catch { Data.ActiveProfile = previousActive; profile.GamePath = previousPath; throw; }
    }

    /// <summary>Allows portable profiles and rejects a profile that belongs to another Neodash installation.</summary>
    public static void EnsureProfileGamePath(ModProfile profile, string win64Path)
    {
        if (profile.GamePath is not null && !SameGame(profile.GamePath, win64Path))
            throw new InvalidOperationException($"The profile \"{profile.Name}\" belongs to a different Neodash installation. Import it again or create a profile for this game.");
    }

    /// <summary>Binds the active portable profile to this game after explicit user intent, without changing its enabled set.</summary>
    public static void BindActiveProfile(string win64Path)
    {
        EnsureWritable();
        var active = ActiveProfile;
        EnsureProfileGamePath(active, win64Path);
        if (active.GamePath is not null) return;
        string normalizedPath = NormalizeGamePath(win64Path);
        active.GamePath = normalizedPath;
        try { SaveProfiles(); }
        catch { active.GamePath = null; throw; }
    }

    /// <summary>Compatibility overload that removes uninstalled folder names from every profile.</summary>
    public static void RemoveMods(IEnumerable<string> names)
    {
        RemoveModsFromProfiles(names, _ => true);
    }

    /// <summary>Removes folders only from profiles for this installation and legacy unbound profiles.</summary>
    public static void RemoveMods(IEnumerable<string> names, string win64Path)
    {
        string normalizedPath = NormalizeGamePath(win64Path);
        RemoveModsFromProfiles(names, profile => profile.GamePath is not null && SameGame(profile.GamePath, normalizedPath));
    }

    private static void RemoveModsFromProfiles(IEnumerable<string> names, Func<ModProfile, bool> appliesTo)
    {
        EnsureWritable();
        var removed = new HashSet<string>(NormalizeMods(names), StringComparer.OrdinalIgnoreCase);
        if (removed.Count == 0) return;
        var affected = Data.Profiles.Where(appliesTo).ToList();
        var previous = affected.ToDictionary(profile => profile, profile => new List<string>(profile.EnabledMods));
        bool changed = false;
        foreach (var profile in affected)
        {
            int before = profile.EnabledMods.Count;
            profile.EnabledMods.RemoveAll(name => removed.Contains(name));
            changed |= profile.EnabledMods.Count != before;
        }
        if (!changed) return;
        try { SaveProfiles(); }
        catch
        {
            foreach (var (profile, enabledMods) in previous)
            {
                profile.EnabledMods.Clear();
                profile.EnabledMods.AddRange(enabledMods);
            }
            throw;
        }
    }

    /// <summary>Exports the active profile and retained metadata, omitting its local game binding for portability.</summary>
    public static void ExportProfile(string path)
    {
        EnsureWritable();
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a profile export path.", nameof(path));
        if (string.Equals(Path.GetFullPath(path), ProfilesPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an export file outside the loader's profiles.json so your other profiles are preserved.");
        var active = ActiveProfile;
        var export = new ModProfileData
        {
            ActiveProfile = active.Name,
            Profiles = [new ModProfile { Name = active.Name, EnabledMods = NormalizeMods(active.EnabledMods), AdditionalData = new(active.AdditionalData) }],
            AdditionalData = new(Data.AdditionalData)
        };
        ValidateLoadedData(export);
        WriteAtomically(path, ProfileJsonFormat.Serialize(export));
    }

    /// <summary>Imports one named enabled-mod set and leaves the current game files untouched.</summary>
    public static ModProfile ImportProfile(string path)
    {
        EnsureWritable();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("The selected profile file was not found.", path);
        ModProfileData imported;
        try
        {
            imported = ProfileJsonFormat.Deserialize(File.ReadAllText(path));
            ValidateLoadedData(imported);
        }
        catch (JsonException ex) { throw new InvalidOperationException($"The selected file could not be imported: {ex.Message}", ex); }
        if (imported.Profiles.Count != 1) throw new InvalidOperationException("A profile export must contain exactly one profile.");
        ModProfile source = imported.Profiles[0];
        string? error = ValidateName(source.Name);
        if (error is not null) throw new InvalidOperationException(error);
        var profile = new ModProfile { Name = source.Name.Trim(), EnabledMods = NormalizeMods(source.EnabledMods), AdditionalData = new(source.AdditionalData) };
        var previousData = Data.AdditionalData;
        var mergedData = MergeImportedMetadata(imported.AdditionalData);
        Data.Profiles.Add(profile);
        Data.AdditionalData = mergedData;
        try { SaveProfiles(); }
        catch { Data.Profiles.Remove(profile); Data.AdditionalData = previousData; throw; }
        return profile;
    }

    private static Dictionary<string, JsonElement> MergeImportedMetadata(Dictionary<string, JsonElement> imported)
    {
        var merged = new Dictionary<string, JsonElement>(Data.AdditionalData);
        foreach (var (key, value) in imported)
        {
            if (merged.TryGetValue(key, out var existing) && !JsonElement.DeepEquals(existing, value))
                throw new InvalidOperationException($"The profile export contains conflicting metadata '{key}'. Neither file has been changed.");
            merged[key] = value;
        }
        return merged;
    }

    public static void SyncActiveProfile(string win64Path)
    {
        string normalizedPath = NormalizeGamePath(win64Path);
        EnsureProfileGamePath(ActiveProfile, normalizedPath);
        var merged = MergeDiskState(ActiveProfile, normalizedPath);
        if (merged.SequenceEqual(ActiveProfile.EnabledMods, StringComparer.OrdinalIgnoreCase) && ActiveProfile.GamePath is not null) return;
        EnsureWritable();
        var previous = new List<string>(ActiveProfile.EnabledMods);
        string? previousPath = ActiveProfile.GamePath;
        ActiveProfile.EnabledMods = merged;
        ActiveProfile.GamePath ??= normalizedPath;
        try { SaveProfiles(); }
        catch { ActiveProfile.EnabledMods = previous; ActiveProfile.GamePath = previousPath; throw; }
    }

    /// <summary>
    /// Reconciles profile folder references after a package changes its top-level folders, then records the active
    /// profile's actual enabled state. Only profiles already bound to this game are affected.
    /// </summary>
    public static void ReconcilePackageFoldersAndSync(string gamePath, IEnumerable<string> oldFolders, IEnumerable<string> newFolders)
    {
        string normalizedPath = NormalizeGamePath(gamePath);
        var oldNames = NormalizeMods(oldFolders);
        var newNames = NormalizeMods(newFolders);
        EnsureWritable();
        EnsureProfileGamePath(ActiveProfile, normalizedPath);

        var affected = Data.Profiles.Where(profile => profile.GamePath is not null && SameGame(profile.GamePath, normalizedPath)).ToList();
        if (!affected.Contains(ActiveProfile)) affected.Add(ActiveProfile);
        var snapshots = affected.ToDictionary(profile => profile, profile => (EnabledMods: new List<string>(profile.EnabledMods), GamePath: profile.GamePath));
        bool changed = false;

        try
        {
            bool rename = oldNames.Count == 1 && newNames.Count == 1 && !string.Equals(oldNames[0], newNames[0], StringComparison.OrdinalIgnoreCase);
            var obsolete = new HashSet<string>(oldNames.Except(newNames, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            foreach (var profile in affected)
            {
                var reconciled = ReconcileFolderNames(profile.EnabledMods, oldNames, newNames, obsolete, rename);
                if (!reconciled.SequenceEqual(profile.EnabledMods, StringComparer.OrdinalIgnoreCase))
                {
                    profile.EnabledMods = reconciled;
                    changed = true;
                }
            }

            // Mod operations bind the active profile before calling this method. Retain the defensive bind so the
            // method remains safe for direct callers and persists it in the same transaction.
            if (ActiveProfile.GamePath is null)
            {
                ActiveProfile.GamePath = normalizedPath;
                changed = true;
            }

            var merged = MergeDiskState(ActiveProfile, normalizedPath);
            if (!merged.SequenceEqual(ActiveProfile.EnabledMods, StringComparer.OrdinalIgnoreCase))
            {
                ActiveProfile.EnabledMods = merged;
                changed = true;
            }

            if (changed) SaveProfiles();
        }
        catch
        {
            foreach (var entry in snapshots)
            {
                entry.Key.EnabledMods = entry.Value.EnabledMods;
                entry.Key.GamePath = entry.Value.GamePath;
            }
            throw;
        }
    }

    private static List<string> ReconcileFolderNames(List<string> enabledMods, List<string> oldNames, List<string> newNames,
        HashSet<string> obsolete, bool rename)
    {
        var reconciled = new List<string>(enabledMods);
        if (rename && reconciled.RemoveAll(name => string.Equals(name, oldNames[0], StringComparison.OrdinalIgnoreCase)) > 0)
            reconciled.Add(newNames[0]);
        else if (obsolete.Count > 0)
            reconciled.RemoveAll(obsolete.Contains);
        return NormalizeMods(reconciled);
    }

    private static List<string> MergeDiskState(ModProfile profile, string normalizedPath)
    {
        var mods = ModManager.GetMods(normalizedPath);
        var installed = new HashSet<string>(mods.Select(mod => mod.Name), StringComparer.OrdinalIgnoreCase);
        var enabled = new HashSet<string>(mods.Where(mod => mod.IsEnabled).Select(mod => mod.Name), StringComparer.OrdinalIgnoreCase);
        return NormalizeMods(profile.EnabledMods.Where(name => !installed.Contains(name) || enabled.Contains(name)).Concat(enabled));
    }
}
