using Neodash_Mod_Loader.Models;
using System.Text.Json.Serialization;

namespace Neodash_Mod_Loader
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppSettings))]
    [JsonSerializable(typeof(ModProfileData))]
    [JsonSerializable(typeof(ModLibraryResponse))]
    internal partial class AppJsonContext : JsonSerializerContext;
}
