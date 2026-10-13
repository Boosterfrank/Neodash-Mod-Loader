using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Neodash_Mod_Loader.Models
{
    public class ModLibraryResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        [JsonPropertyName("mods")]
        public List<ModLibraryItem> Mods { get; set; }
    }

    public class ModLibraryItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("developerName")]
        public string DeveloperName { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("downloadURL")]
        public string DownloadURL { get; set; }
    }
}
