using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Neodash_Mod_Loader
{
    /// <summary>
    /// Enables and disables UE4SS mods by moving their folders between Win64\Mods and Win64\DisabledMods.
    /// UE4SS loads mods with an enabled.txt no matter what mods.txt says, so folder location is the only
    /// reliable on/off switch.
    /// </summary>
    public static class ModManager
    {
        public const string ModsFolderName = "Mods";
        public const string DisabledModsFolderName = "DisabledMods";

        // UE4SS support folders that live in Mods but aren't user mods, so they're never listed or moved.
        private static readonly HashSet<string> ReservedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Keybinds", "shared" };

        public static string GetModPath(string win64Path, string modName, bool enabled)
        {
            return Path.Combine(win64Path, enabled ? ModsFolderName : DisabledModsFolderName, modName);
        }

        public static bool IsModInstalled(string win64Path, string modName)
        {
            return Directory.Exists(GetModPath(win64Path, modName, true)) || Directory.Exists(GetModPath(win64Path, modName, false));
        }

        /// <summary>
        /// Lists mods in Mods (enabled) and DisabledMods (disabled). A folder present in both is reported once
        /// as enabled, since UE4SS loads the copy in Mods.
        /// </summary>
        public static List<(string Name, bool IsEnabled)> GetMods(string win64Path)
        {
            var mods = new List<(string Name, bool IsEnabled)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (bool enabled in new[] { true, false })
            {
                string folderPath = Path.Combine(win64Path, enabled ? ModsFolderName : DisabledModsFolderName);
                if (!Directory.Exists(folderPath)) continue;

                foreach (var name in Directory.GetDirectories(folderPath).Select(Path.GetFileName))
                {
                    if (!ReservedFolders.Contains(name) && seen.Add(name))
                    {
                        mods.Add((name, enabled));
                    }
                }
            }

            return mods;
        }

        /// <summary>
        /// Moves a mod's folder into Mods (enabled) or DisabledMods (disabled). Throws if the folder is missing,
        /// the other location already has a folder with that name, or files are locked because the game is running.
        /// </summary>
        public static void SetModEnabled(string win64Path, string modName, bool enabled)
        {
            if (!IsValidModFolderName(modName))
            {
                throw new ArgumentException($"\"{modName}\" is not a mod folder that can be moved.");
            }

            string source = GetModPath(win64Path, modName, !enabled);
            string destination = GetModPath(win64Path, modName, enabled);

            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException($"Folder not found: {source}");
            }
            if (Directory.Exists(destination))
            {
                throw new IOException($"A folder with the same name already exists: {destination}");
            }

            if (enabled)
            {
                // Lua mods without an enabled.txt still need a mods.txt entry to load. Done before the move because
                // UE4SS skips entries whose folder isn't in Mods, so a failed move leaves nothing inconsistent.
                EnableInModsTxt(win64Path, new[] { modName });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            Directory.Move(source, destination);
        }

        /// <summary>
        /// Enables exactly the given mods and disables every other installed mod. If any move fails, the moves already
        /// made are undone so mods aren't left half-switched. Returns one message per mod that failed.
        /// </summary>
        public static List<string> ApplyEnabledMods(string win64Path, IEnumerable<string> enabledMods)
        {
            var wanted = new HashSet<string>(enabledMods, StringComparer.OrdinalIgnoreCase);
            var moved = new List<(string Name, bool Enabled)>();
            var errors = new List<string>();

            foreach (var mod in GetMods(win64Path))
            {
                bool enable = wanted.Contains(mod.Name);
                if (enable == mod.IsEnabled) continue;

                try
                {
                    SetModEnabled(win64Path, mod.Name, enable);
                    moved.Add((mod.Name, enable));
                }
                catch (Exception ex)
                {
                    errors.Add($"{mod.Name}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                foreach (var mod in moved)
                {
                    try
                    {
                        SetModEnabled(win64Path, mod.Name, !mod.Enabled);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{mod.Name} (couldn't be moved back): {ex.Message}");
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Extracts a mod zip into Mods and enables each top-level folder it contains. A disabled copy of the same
        /// mod is moved back into Mods first, so the zip updates it in place instead of leaving a duplicate behind.
        /// </summary>
        public static void InstallModZip(string win64Path, string zipPath)
        {
            string modsPath = Path.Combine(win64Path, ModsFolderName);
            Directory.CreateDirectory(modsPath);

            var modFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    var parts = entry.FullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        modFolders.Add(parts[0]);
                    }
                }
            }

            foreach (var modFolder in modFolders)
            {
                if (IsValidModFolderName(modFolder) &&
                    Directory.Exists(GetModPath(win64Path, modFolder, false)) &&
                    !Directory.Exists(GetModPath(win64Path, modFolder, true)))
                {
                    SetModEnabled(win64Path, modFolder, true);
                }
            }

            ZipFile.ExtractToDirectory(zipPath, modsPath, overwriteFiles: true);
            EnableInModsTxt(win64Path, modFolders);
        }

        /// <summary>
        /// Sets each mod to "Name : 1" in mods.txt, adding missing entries above the built-in Keybinds block.
        /// Does nothing if mods.txt doesn't exist.
        /// </summary>
        public static void EnableInModsTxt(string win64Path, IEnumerable<string> modNames)
        {
            string modsTxtPath = Path.Combine(win64Path, ModsFolderName, "mods.txt");
            if (!File.Exists(modsTxtPath)) return;

            var lines = new List<string>(File.ReadAllLines(modsTxtPath));
            bool changed = false;

            foreach (var modName in modNames)
            {
                string enabledLine = $"{modName} : 1";
                int index = FindModsTxtEntry(lines, modName);

                if (index < 0)
                {
                    lines.Insert(FindKeybindsBlockStart(lines), enabledLine);
                    changed = true;
                }
                else if (lines[index] != enabledLine)
                {
                    lines[index] = enabledLine;
                    changed = true;
                }
            }

            if (changed)
            {
                File.WriteAllLines(modsTxtPath, lines);
            }
        }

        /// <summary>
        /// One-time migration from older loader versions, which disabled mods by writing "Name : 0" to mods.txt.
        /// Moves those mods into DisabledMods so they stay disabled. Returns false if any couldn't be moved
        /// (e.g. the game is running) so the caller can retry on a later launch.
        /// </summary>
        public static bool MigrateModsTxtDisabledMods(string win64Path)
        {
            string modsTxtPath = Path.Combine(win64Path, ModsFolderName, "mods.txt");
            if (!File.Exists(modsTxtPath)) return true;

            bool allMoved = true;
            foreach (var line in File.ReadAllLines(modsTxtPath))
            {
                if (!TryParseModsTxtEntry(line, out string name, out string value) || value != "0") continue;
                if (!IsValidModFolderName(name) || !Directory.Exists(GetModPath(win64Path, name, true))) continue;

                try
                {
                    SetModEnabled(win64Path, name, false);
                }
                catch (Exception)
                {
                    allMoved = false;
                }
            }

            return allMoved;
        }

        // Rejects reserved folders and anything that isn't a plain folder name (e.g. "..").
        private static bool IsValidModFolderName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name != "." && name != ".." &&
                   name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   !ReservedFolders.Contains(name);
        }

        // Parses a "Name : 1" line. Comments and anything without exactly one colon aren't entries.
        private static bool TryParseModsTxtEntry(string line, out string name, out string value)
        {
            var parts = line.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !line.TrimStart().StartsWith(";"))
            {
                name = parts[0];
                value = parts[1];
                return true;
            }

            name = null;
            value = null;
            return false;
        }

        private static int FindModsTxtEntry(List<string> lines, string modName)
        {
            return lines.FindIndex(line => TryParseModsTxtEntry(line, out string name, out _) && name.Equals(modName, StringComparison.OrdinalIgnoreCase));
        }

        // Where new entries go: above "Keybinds : 1", along with the comment and blank line UE4SS puts before it.
        private static int FindKeybindsBlockStart(List<string> lines)
        {
            int index = FindModsTxtEntry(lines, "Keybinds");
            if (index < 0) return lines.Count;

            if (index > 0 && lines[index - 1].TrimStart().StartsWith(";") && lines[index - 1].Contains("keybinds", StringComparison.OrdinalIgnoreCase))
            {
                index--;
            }
            if (index > 0 && string.IsNullOrWhiteSpace(lines[index - 1]))
            {
                index--;
            }

            return index;
        }
    }
}
