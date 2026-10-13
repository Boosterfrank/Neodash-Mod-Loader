using Neodash_Mod_Loader.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Neodash_Mod_Loader
{
    /// <summary>
    /// Stores mod profiles: named sets of enabled mods that all share the same installed mod folders.
    /// The active profile always mirrors what's on disk; switching applies another profile's set.
    /// </summary>
    public static class ProfileManager
    {
        public const int MaxNameLength = 40;
        private const string DefaultProfileName = "Default";

        private static readonly string ProfilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Neodash Mod Loader",
            "profiles.json");

        private static readonly ModProfileData Data = LoadProfiles();

        public static IReadOnlyList<ModProfile> Profiles => Data.Profiles;

        public static ModProfile ActiveProfile => Data.Profiles.First(p => p.Name == Data.ActiveProfile);

        private static ModProfileData LoadProfiles()
        {
            ModProfileData data = null;
            try
            {
                if (File.Exists(ProfilesPath))
                {
                    data = JsonSerializer.Deserialize<ModProfileData>(File.ReadAllText(ProfilesPath));
                }
            }
            catch
            {
                // Corrupt file: start over with a fresh Default profile, which the next sync fills from disk.
            }

            data ??= new ModProfileData();

            // Drop hand-edited entries with blank, overlong or duplicate names (keeping the first of each).
            var validProfiles = new List<ModProfile>();
            foreach (var profile in data.Profiles ?? new List<ModProfile>())
            {
                if (profile == null || ValidateName(profile.Name, validProfiles, null) != null) continue;

                profile.Name = profile.Name.Trim();
                profile.EnabledMods ??= new List<string>();
                validProfiles.Add(profile);
            }
            data.Profiles = validProfiles;

            if (data.Profiles.Count == 0)
            {
                data.Profiles.Add(new ModProfile { Name = DefaultProfileName });
            }
            if (!data.Profiles.Any(p => p.Name == data.ActiveProfile))
            {
                data.ActiveProfile = data.Profiles[0].Name;
            }

            return data;
        }

        private static void SaveProfiles()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath));
                File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Ignore save errors, same as SettingsManager
            }
        }

        /// <summary>
        /// Returns why a name can't be used, or null if it's fine. Pass the profile being renamed so it doesn't clash with itself.
        /// </summary>
        public static string ValidateName(string name, ModProfile renaming = null)
        {
            return ValidateName(name, Data.Profiles, renaming);
        }

        private static string ValidateName(string name, IEnumerable<ModProfile> profiles, ModProfile renaming)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return "Enter a name.";
            if (name.Length > MaxNameLength) return $"Use {MaxNameLength} characters or fewer.";
            if (profiles.Any(p => p != renaming && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return "A profile with that name already exists.";
            }
            return null;
        }

        /// <summary>
        /// Creates a profile with the same mods as the active one and makes it active, so no folders need to move.
        /// </summary>
        public static ModProfile CreateProfile(string name)
        {
            var profile = new ModProfile { Name = name.Trim(), EnabledMods = new List<string>(ActiveProfile.EnabledMods) };
            Data.Profiles.Add(profile);
            Data.ActiveProfile = profile.Name;
            SaveProfiles();
            return profile;
        }

        public static void RenameProfile(ModProfile profile, string newName)
        {
            bool isActive = profile == ActiveProfile;
            profile.Name = newName.Trim();
            if (isActive)
            {
                Data.ActiveProfile = profile.Name;
            }
            SaveProfiles();
        }

        /// <summary>
        /// Deletes an inactive profile. Switch to another profile before deleting the active one.
        /// </summary>
        public static void DeleteProfile(ModProfile profile)
        {
            if (profile == ActiveProfile)
            {
                throw new InvalidOperationException("Can't delete the active profile.");
            }
            Data.Profiles.Remove(profile);
            SaveProfiles();
        }

        /// <summary>
        /// Marks a profile as active. Call after its mods have been applied with ModManager.ApplyEnabledMods.
        /// </summary>
        public static void SetActiveProfile(ModProfile profile)
        {
            Data.ActiveProfile = profile.Name;
            SaveProfiles();
        }

        /// <summary>
        /// Records the mods currently enabled on disk into the active profile, picking up saved toggles,
        /// library installs, and folders moved by hand.
        /// </summary>
        public static void SyncActiveProfile(string win64Path)
        {
            // If the game folder can't be found, keep the profile rather than wiping it.
            if (!Directory.Exists(Path.Combine(win64Path, ModManager.ModsFolderName))) return;

            var enabledMods = ModManager.GetMods(win64Path)
                .Where(m => m.IsEnabled)
                .Select(m => m.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!enabledMods.SequenceEqual(ActiveProfile.EnabledMods))
            {
                ActiveProfile.EnabledMods = enabledMods;
                SaveProfiles();
            }
        }
    }
}
