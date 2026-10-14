using System.Text;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static void OldVersionSettingsResaveCompatibility(string scratch)
    {
        var paths = NewGame(scratch, "settings-round-trip");
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        var settings = new AppSettings
        {
            AppTheme = "Light", IsSetupComplete = true, ModsTxtMigrated = true, GameDirectory = paths.Game,
            Installations = [new InstalledModRecord
            {
                Id = "example", Name = "Example", Version = "3.0.0", GamePath = paths.Game,
                FolderNames = ["Example", "Support"], DownloadUrl = "https://example.invalid/mod.zip",
                Sha256 = new string('a', 64), BackupPath = paths.Backups, BackupVersion = "2.0.0"
            }],
            Ue4ssInstallations = [new Ue4SsInstallationRecord
            {
                GamePath = paths.Game, Version = "3.0.1", Files = ["dwmapi.dll", "ue4ss/UE4SS.dll"]
            }],
            InstalledMods = new() { ["Example"] = "3.0.0" },
            AdditionalSettings = new()
            {
                ["InstalledMod_Legacy"] = JsonSerializer.SerializeToElement("1.2.3"),
                ["UnknownFlag"] = JsonSerializer.SerializeToElement(new { keep = true }),
                ["JsonLookingText"] = JsonSerializer.SerializeToElement("[not a collection]")
            }
        };
        string currentJson = JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings);

        for (int roundTrip = 0; roundTrip < 3; roundTrip++)
        {
            string legacyJson = ResaveSettingsLikeVersion13(currentJson);
            byte[] original = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(legacyJson)];
            File.WriteAllBytes(settingsPath, original);
            SettingsManager.LoadSettings();

            Assert(SettingsManager.LoadError is null, "new settings resaved by version 1.3 load successfully");
            Assert(File.ReadAllBytes(settingsPath).SequenceEqual(original), "loading migration preserves the original file until a normal save");
            var loaded = SettingsManager.Current;
            Assert(loaded.AppTheme == "Light" && loaded.IsSetupComplete && loaded.ModsTxtMigrated && loaded.GameDirectory == paths.Game,
                "round trips preserve theme, setup, migration state, and game selection");
            var installation = loaded.Installations.Single();
            Assert(installation.Id == "example" && installation.Name == "Example" && installation.Version == "3.0.0"
                && installation.GamePath == paths.Game && installation.FolderNames.SequenceEqual(["Example", "Support"])
                && installation.DownloadUrl == "https://example.invalid/mod.zip" && installation.Sha256 == new string('a', 64)
                && installation.BackupPath == paths.Backups && installation.BackupVersion == "2.0.0",
                "round trips preserve every installed-package ownership and recovery field");
            var runtime = loaded.Ue4ssInstallations.Single();
            Assert(runtime.GamePath == paths.Game && runtime.Version == "3.0.1" && runtime.Files.SequenceEqual(["dwmapi.dll", "ue4ss/UE4SS.dll"]),
                "round trips preserve runtime ownership and version");
            Assert(loaded.GetInstalledModVersion("Example") == "3.0.0" && loaded.GetInstalledModVersion("Legacy") == "1.2.3",
                "round trips preserve modern and legacy installed versions");
            using var legacyDocument = JsonDocument.Parse(legacyJson);
            Assert(loaded.AdditionalSettings["UnknownFlag"].ValueKind == JsonValueKind.String
                && loaded.AdditionalSettings["UnknownFlag"].GetString() == legacyDocument.RootElement.GetProperty("UnknownFlag").GetString()
                && loaded.AdditionalSettings["JsonLookingText"].GetString() == "[not a collection]",
                "migration preserves unknown values exactly as the old writer stored them without interpreting arbitrary strings");

            SettingsManager.SaveSettings();
            currentJson = File.ReadAllText(settingsPath);
            using var saved = JsonDocument.Parse(currentJson);
            Assert(saved.RootElement.GetProperty("Installations").ValueKind == JsonValueKind.Array
                && saved.RootElement.GetProperty("Ue4ssInstallations").ValueKind == JsonValueKind.Array
                && saved.RootElement.GetProperty("InstalledMods").ValueKind == JsonValueKind.Object,
                "normal saves upgrade legacy string fields to native arrays and objects");
            SettingsManager.LoadSettings();
            Assert(SettingsManager.LoadError is null, "the saved current format can be reopened");
        }
    }

    private static string ResaveSettingsLikeVersion13(string json)
    {
        // Reproduces 1.3's Dictionary<string, object> normalization and save without adding the old app as a dependency.
        using var source = JsonDocument.Parse(json);
        var values = new Dictionary<string, object?>();
        foreach (var property in source.RootElement.EnumerateObject())
        {
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetDouble(),
                _ => property.Value.ToString()
            };
        }
        return JsonSerializer.Serialize(values);
    }

    private static void MixedAndEmptySettingsFormats()
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        string[] documents =
        [
            """{"Installations":[],"Ue4ssInstallations":"[]","InstalledMods":"{}"}""",
            """{"Installations":"[]","Ue4ssInstallations":[],"InstalledMods":{}}""",
            """{"Installations":null,"Ue4ssInstallations":null,"InstalledMods":null}""",
            """{"Installations":"null","Ue4ssInstallations":"null","InstalledMods":"null"}""",
            """{"UnknownText":"[]","UnknownObject":{"keep":true}}"""
        ];
        foreach (string json in documents)
        {
            File.WriteAllText(settingsPath, json);
            SettingsManager.LoadSettings();
            Assert(SettingsManager.LoadError is null, "mixed current/legacy collection fields and empty or null collections are supported");
            Assert(SettingsManager.Current.Installations.Count == 0 && SettingsManager.Current.Ue4ssInstallations.Count == 0
                && SettingsManager.Current.InstalledMods.Count == 0, "empty collection formats normalize without inventing installation records");
        }
        Assert(SettingsManager.Current.AdditionalSettings["UnknownText"].GetString() == "[]"
            && SettingsManager.Current.AdditionalSettings["UnknownObject"].ValueKind == JsonValueKind.Object,
            "unknown strings and native objects retain their types");
    }

    private static void InvalidLegacyCollectionSettings(string scratch)
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        string invalidRecord = JsonSerializer.Serialize(new[] { new InstalledModRecord
        {
            Id = "unsafe", Name = "Unsafe", Version = "1", GamePath = scratch, FolderNames = [".."]
        } });
        var invalidValues = new (string Name, string Encoded)[]
        {
            ("Installations", "{"), ("Ue4ssInstallations", "{"), ("InstalledMods", "{"),
            ("Installations", "{}"), ("Ue4ssInstallations", "{}"), ("InstalledMods", "[]"),
            ("Installations", "[null]"), ("Ue4ssInstallations", "[null]"), ("Installations", invalidRecord),
            ("Installations", "\"[]\"")
        };
        foreach (var (name, encoded) in invalidValues)
        {
            byte[] original = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, string> { [name] = encoded }));
            File.WriteAllBytes(settingsPath, original);
            SettingsManager.LoadSettings();
            Assert(SettingsManager.LoadError is not null, "malformed or invalid string-encoded records remain blocked");
            Assert(File.ReadAllBytes(settingsPath).SequenceEqual(original), "failed legacy decoding preserves exact settings bytes");
            AssertThrows<InvalidOperationException>(SettingsManager.SaveSettings, "failed legacy decoding cannot overwrite settings");
        }
        File.WriteAllText(settingsPath, "{}");
        SettingsManager.LoadSettings();
        Assert(SettingsManager.LoadError is null, "a repaired settings document can still be loaded");
    }
}
