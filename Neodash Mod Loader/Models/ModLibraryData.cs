using System.Text.Json.Serialization;
using System.Text.Json;

namespace Neodash_Mod_Loader.Models
{
    public class ModLibraryResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; set; }

        [JsonPropertyName("mods")]
        public List<ModLibraryItem>? Mods { get; set; }

        public void Validate()
        {
            if (Mods is null) throw new JsonException("The mod library does not contain a mods list.");
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in Mods)
            {
                ValidateModEntry(mod, identities, folders);
            }
        }

        private static void ValidateModEntry(ModLibraryItem? mod, HashSet<string> identities, HashSet<string> folders)
        {
            if (mod is null || string.IsNullOrWhiteSpace(mod.Name)
                || string.IsNullOrWhiteSpace(mod.Version) || string.IsNullOrWhiteSpace(mod.DownloadURL))
            {
                throw new JsonException("A mod library entry is missing its name, version, or download URL.");
            }
            if (!identities.Add(mod.Id ?? mod.Name)) throw new JsonException("The mod library contains duplicate mod identities.");
            mod.DeveloperName ??= string.Empty;
            mod.Subtitle ??= string.Empty;
            if (!Uri.TryCreate(mod.DownloadURL, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                throw new JsonException("A mod download address must use HTTPS.");
            ValidateFolderNames(mod, folders);
            if (mod.Sha256 is not null && (mod.Sha256.Length != 64 || mod.Sha256.Any(c => !Uri.IsHexDigit(c))))
                throw new JsonException("A mod download has an invalid SHA-256 checksum.");
        }

        private static void ValidateFolderNames(ModLibraryItem mod, HashSet<string> folders)
        {
            if (mod.FolderNames is null) return;
            foreach (string folder in mod.FolderNames)
            {
                if (string.IsNullOrWhiteSpace(folder) || folder is "." or ".." || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || folder.Equals("Keybinds", StringComparison.OrdinalIgnoreCase) || folder.Equals("shared", StringComparison.OrdinalIgnoreCase))
                    throw new JsonException("The mod library contains an invalid mod folder name.");
                if (!folders.Add(folder)) throw new JsonException("The mod library assigns the same folder to more than one entry.");
            }
            if (mod.FolderNames.Count == 0) mod.FolderNames = null;
        }
    }

    public class ModLibraryItem
    {
        [JsonPropertyName("name")]
        [JsonRequired]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("developerName")]
        public string DeveloperName { get; set; } = string.Empty;

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        [JsonRequired]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("downloadURL")]
        [JsonRequired]
        public string DownloadURL { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("folderNames")]
        public List<string>? FolderNames { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }
}
