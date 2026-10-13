using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Neodash_Mod_Loader.Models
{
    public class ModProfile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        // Folder names of the mods this profile enables. Every other installed mod is disabled.
        [JsonPropertyName("enabledMods")]
        public List<string> EnabledMods { get; set; } = new List<string>();
    }

    public class ModProfileData
    {
        [JsonPropertyName("activeProfile")]
        public string ActiveProfile { get; set; }

        [JsonPropertyName("profiles")]
        public List<ModProfile> Profiles { get; set; } = new List<ModProfile>();
    }
}
