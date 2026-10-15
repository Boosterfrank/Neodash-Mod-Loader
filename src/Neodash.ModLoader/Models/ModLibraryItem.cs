using System.Text.Json.Serialization;
using System.Text.Json;

namespace Neodash_Mod_Loader.Models;
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
