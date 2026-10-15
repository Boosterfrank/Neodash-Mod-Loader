using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neodash_Mod_Loader.Models;
public class ModProfileData
{
    // Storage versions change only for incompatible contract changes, independently of app releases.
    public const int CurrentSchemaVersion = 1;
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("activeProfile")]
    public string? ActiveProfile { get; set; }

    [JsonPropertyName("profiles")]
    public List<ModProfile> Profiles { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalData { get; set; } = [];
}
