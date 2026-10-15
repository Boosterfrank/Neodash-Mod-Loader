using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neodash_Mod_Loader.Models;
public class ModProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // Folder names of the mods this profile enables. Every other installed mod is disabled.
    [JsonPropertyName("enabledMods")]
    public List<string> EnabledMods { get; set; } = [];

    // Null means a legacy or imported portable profile. A bound profile can only be applied to its original Win64 folder.
    [JsonPropertyName("gamePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GamePath { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}
