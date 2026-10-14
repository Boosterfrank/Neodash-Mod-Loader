using System.Text.Json;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

/// <summary>The shared storage contract for profiles.json and portable profile exports.</summary>
internal static class ProfileJsonFormat
{
    internal static ModProfileData Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Profiles must contain a JSON object.");

        int schemaVersion = 1; // Unversioned files use the original profile schema, even after future schema bumps.
        if (root.TryGetProperty("schemaVersion", out var schema)
            && (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out schemaVersion)))
            throw new JsonException("Profile schemaVersion must be an integer.");
        ValidateSchemaVersion(schemaVersion);

        return JsonSerializer.Deserialize(root, AppJsonContext.Default.ModProfileData)
            ?? throw new JsonException("Profiles must contain a JSON object.");
    }

    internal static string Serialize(ModProfileData data)
    {
        ValidateSchemaVersion(data.SchemaVersion);
        return JsonSerializer.Serialize(data, AppJsonContext.Default.ModProfileData);
    }

    private static void ValidateSchemaVersion(int version)
    {
        if (version > ModProfileData.CurrentSchemaVersion)
            throw new JsonException($"Profile format {version} requires a newer version of Neodash Mod Loader. Your profiles will not be overwritten.");
        if (version < 1)
            throw new JsonException("Profile schemaVersion must be a positive integer.");
        if (version != ModProfileData.CurrentSchemaVersion)
            throw new JsonException($"Profile format {version} needs a migration that this build does not support. Your profiles will not be overwritten.");
    }
}
