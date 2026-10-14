using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static void LegacyProfileMigration(string scratch)
    {
        string path = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        string gamePath = NewGame(scratch, "profile-schema").Game;
        string original = $$$"""
            {
              "activeProfile": "Workshop",
              "profiles": [
                { "name": "Portable 旧", "enabledMods": ["Alpha", "Unavailable"], "notes": {"keep":[true,null,3]}, "label":"{\"leave\":true}" },
                { "name": "Workshop", "enabledMods": ["NativeMod", "UninstalledButSelected"], "gamePath": {{{JsonSerializer.Serialize(gamePath)}}}, "flags":["custom",7] }
              ],
              "futureMetadata": {"enabled":true,"items":[1,null,"text"]},
              "literalJson": "[1,2,3]"
            }
            """;
        File.WriteAllText(path, original);
        byte[] originalBytes = File.ReadAllBytes(path);

        ProfileManager.EnsureWritable();
        Assert(ProfileManager.Profiles.Count == 2 && ProfileManager.ActiveProfile.Name == "Workshop", "legacy loading retains every profile and the selected profile");
        Assert(ProfileManager.Profiles[0].GamePath is null && ProfileManager.ActiveProfile.GamePath == gamePath, "portable profiles and existing game bindings remain distinct");
        Assert(File.ReadAllBytes(path).SequenceEqual(originalBytes), "loading migrates in memory without replacing the legacy file");

        var expected = JsonNode.Parse(original)!.AsObject();
        expected["schemaVersion"] = 1;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            ProfileManager.SetActiveProfile(ProfileManager.ActiveProfile);
            byte[] saved = File.ReadAllBytes(path);
            Assert(!saved.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "profiles use canonical UTF-8 without a byte-order mark");
            Assert(JsonNode.DeepEquals(expected, JsonNode.Parse(saved)), "normal saves retain names, all selections including unavailable mods, bindings, and root/profile metadata across repeated writes");
            ModProfileData reopened = ProfileJsonFormat.Deserialize(Encoding.UTF8.GetString(saved));
            Assert(reopened.SchemaVersion == 1 && reopened.Profiles.Count == 2 && reopened.ActiveProfile == "Workshop", "the saved schema remains readable with all profiles present");
        }
        byte[] beforeExport = File.ReadAllBytes(path);
        AssertThrows<InvalidOperationException>(() => ProfileManager.ExportProfile(path), "export cannot replace the profile store with only the active profile");
        Assert(File.ReadAllBytes(path).SequenceEqual(beforeExport), "rejecting an export over profiles.json preserves every stored profile");
    }

    private static void ProfileMetadataRoundTrip(string scratch)
    {
        string path = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        File.WriteAllText(path, """{"schemaVersion":1,"activeProfile":"Default","profiles":[{"name":"Default","enabledMods":["Keep"],"original":true}],"shared":{"keep":true}}""");
        ProfileManager.EnsureWritable();
        string importPath = Path.Combine(scratch, "versioned-profile.json");
        string game = NewGame(scratch, "import-source-game").Game;
        string importedJson = $$$"""
            {"schemaVersion":1,"activeProfile":"Imported","profiles":[{"name":"Imported","enabledMods":["Alpha","Missing"],"gamePath":{{{JsonSerializer.Serialize(game)}}},"custom":{"nested":[false,null,"keep"]}}],"shared":{"keep":true},"importMetadata":[1,2,3]}
            """;
        File.WriteAllText(importPath, importedJson);
        ModProfile imported = ProfileManager.ImportProfile(importPath);
        Assert(imported.GamePath is null && imported.EnabledMods.SequenceEqual(["Alpha", "Missing"]), "a versioned import remains portable and retains unavailable selections");
        Assert(ProfileManager.ActiveProfile.Name == "Default" && ProfileManager.Profiles.Count == 2, "import preserves existing profiles and the active selection");
        ProfileManager.SetActiveProfile(imported);
        string exportPath = Path.Combine(scratch, "versioned-export.json");
        ProfileManager.ExportProfile(exportPath);
        using var exported = JsonDocument.Parse(File.ReadAllBytes(exportPath));
        var root = exported.RootElement;
        Assert(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("profiles").GetArrayLength() == 1, "exports use the same versioned envelope and contain exactly the selected profile");
        Assert(!root.GetProperty("profiles")[0].TryGetProperty("gamePath", out _), "export omits the local game binding intentionally");
        using var source = JsonDocument.Parse(importedJson);
        Assert(JsonElement.DeepEquals(root.GetProperty("importMetadata"), source.RootElement.GetProperty("importMetadata"))
            && JsonElement.DeepEquals(root.GetProperty("profiles")[0].GetProperty("custom"), source.RootElement.GetProperty("profiles")[0].GetProperty("custom")),
            "import and export preserve unknown envelope and profile metadata");
        ProfileManager.SetActiveProfile(ProfileManager.Profiles[0]);
        ProfileManager.DeleteProfile(imported);
        ModProfile reopened = ProfileManager.ImportProfile(exportPath);
        ProfileManager.RenameProfile(reopened, "Renamed");
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert(saved.RootElement.GetProperty("profiles")[0].GetProperty("original").GetBoolean()
            && JsonElement.DeepEquals(saved.RootElement.GetProperty("profiles")[1].GetProperty("custom"), source.RootElement.GetProperty("profiles")[0].GetProperty("custom")),
            "reimport and rename preserve both existing and imported profile metadata");
        Assert(File.ReadAllText(importPath) == importedJson, "import never rewrites its source export");
    }

    private static void ProfileImportRollback(string scratch)
    {
        string path = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        File.WriteAllText(path, """{"schemaVersion":1,"activeProfile":"Default","profiles":[{"name":"Default","enabledMods":[]}],"shared":{"keep":true}}""");
        ProfileManager.EnsureWritable();
        byte[] before = File.ReadAllBytes(path);
        string importPath = Path.Combine(scratch, "import-rollback.json");
        const string source = """{"schemaVersion":1,"activeProfile":"Imported","profiles":[{"name":"Imported","enabledMods":["Missing"]}],"newMetadata":{"keep":true}}""";
        File.WriteAllText(importPath, source);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertThrows<UnauthorizedAccessException>(() => ProfileManager.ImportProfile(importPath), "a failed atomic write surfaces the profile import failure");
        }
        Assert(ProfileManager.Profiles.Count == 1 && ProfileManager.ActiveProfile.Name == "Default" && File.ReadAllBytes(path).SequenceEqual(before), "failed import restores in-memory profiles, active selection, and original persisted bytes");
        ProfileManager.SetActiveProfile(ProfileManager.ActiveProfile);
        using (var saved = JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            Assert(!saved.RootElement.TryGetProperty("newMetadata", out _), "failed import also rolls back merged unknown metadata before a later successful save");
        }
        before = File.ReadAllBytes(path);
        string conflict = source.Replace("\"newMetadata\"", "\"shared\"", StringComparison.Ordinal).Replace("true", "false", StringComparison.Ordinal);
        File.WriteAllText(importPath, conflict);
        AssertThrows<InvalidOperationException>(() => ProfileManager.ImportProfile(importPath), "conflicting unknown metadata is rejected instead of silently replacing either value");
        Assert(ProfileManager.Profiles.Count == 1 && File.ReadAllBytes(path).SequenceEqual(before) && File.ReadAllText(importPath) == conflict, "a metadata conflict leaves both files and the profile list unchanged");
        File.WriteAllText(importPath, source);
        ModProfile imported = ProfileManager.ImportProfile(importPath);
        Assert(imported.EnabledMods.SequenceEqual(["Missing"]) && ProfileManager.Profiles.Count == 2, "the same import succeeds after the write obstruction is removed");
    }

    private static void ProfileImportSchemaGuard(string scratch)
    {
        ProfileManager.CreateProfile("Existing");
        string path = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        byte[] before = File.ReadAllBytes(path);
        string importPath = Path.Combine(scratch, "unsupported-profile.json");
        foreach (string schema in new[] { "2", "0", "-1", "null", "\"1\"", "1.5", "2147483648" })
        {
            string source = "{\"schemaVersion\":" + schema + ",\"profiles\":{\"future\":true}}";
            File.WriteAllText(importPath, source);
            try
            {
                ProfileManager.ImportProfile(importPath);
                throw new InvalidOperationException("An unsupported profile export was imported.");
            }
            catch (InvalidOperationException ex) when (ex.InnerException is JsonException)
            {
                if (schema == "2")
                    Assert(ex.InnerException.Message.Contains("newer version", StringComparison.Ordinal), "future export schemas are rejected before incompatible profile contents are read");
            }
            Assert(File.ReadAllBytes(path).SequenceEqual(before) && File.ReadAllText(importPath) == source
                && ProfileManager.Profiles.Count == 2 && ProfileManager.ActiveProfile.Name == "Existing",
                "invalid and unsupported imports preserve both files, existing profiles, and selection");
        }
    }
}
