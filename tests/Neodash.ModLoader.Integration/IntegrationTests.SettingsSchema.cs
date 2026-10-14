using System.Text;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static void SettingsWriteContract()
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        SettingsManager.RestoreSnapshot(new AppSettings
        {
            AdditionalSettings = new() { ["FuturePreference"] = JsonSerializer.SerializeToElement(new { keep = true }) }
        });

        byte[] savedBytes = File.ReadAllBytes(settingsPath);
        Assert(!savedBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "canonical settings use UTF-8 without a byte-order mark");
        using var saved = JsonDocument.Parse(savedBytes);
        var root = saved.RootElement;
        Assert(root.GetProperty("SchemaVersion").GetInt32() == 1, "app releases keep the version-one settings contract until an explicit schema migration");
        Assert(root.GetProperty("Installations").ValueKind == JsonValueKind.Array
            && root.GetProperty("Ue4ssInstallations").ValueKind == JsonValueKind.Array
            && root.GetProperty("InstalledMods").ValueKind == JsonValueKind.Object,
            "all canonical writes use real arrays and objects, never encoded JSON strings");
        Assert(root.GetProperty("AppTheme").ValueKind == JsonValueKind.String
            && root.GetProperty("IsSetupComplete").ValueKind == JsonValueKind.False
            && root.GetProperty("ModsTxtMigrated").ValueKind == JsonValueKind.False
            && root.GetProperty("GameDirectory").ValueKind == JsonValueKind.Null,
            "existing preference keys and scalar types remain stable");
        SettingsManager.LoadSettings();
        Assert(SettingsManager.LoadError is null && SettingsManager.Current.SchemaVersion == 1
            && SettingsManager.Current.AdditionalSettings["FuturePreference"].GetProperty("keep").GetBoolean(),
            "canonical settings reopen while preserving compatible unknown fields");
        Assert(SettingsManager.CaptureSnapshot().SchemaVersion == 1, "transaction snapshots retain the storage version");
    }

    private static void UnsupportedSettingsSchemas()
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        foreach (string schema in new[] { "2", "0", "-1", "null", "\"1\"", "1.5", "2147483648" })
        {
            // Future contents intentionally disagree with version-one types: the schema gate must run first.
            string json = "{\"SchemaVersion\":" + schema + ",\"Installations\":{\"future\":true},\"Untouched\":[1,2,3]}";
            byte[] original = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(settingsPath, original);
            SettingsManager.LoadSettings();
            Assert(SettingsManager.LoadError is not null, "unsupported or invalid schema versions block loading");
            Assert(File.ReadAllBytes(settingsPath).SequenceEqual(original), "unsupported schemas retain original bytes");
            AssertThrows<InvalidOperationException>(SettingsManager.SaveSettings, "an older reader cannot overwrite unsupported settings");
            if (schema == "2")
                Assert(SettingsManager.LoadError?.Contains("newer version", StringComparison.Ordinal) == true, "future schema failures explain that a newer loader is required before parsing incompatible contents");
        }
        File.WriteAllText(settingsPath, "{}");
        SettingsManager.LoadSettings();
        Assert(SettingsManager.LoadError is null && SettingsManager.Current.SchemaVersion == 1, "unversioned settings stay readable as schema one");
    }

    private static void SettingsSaveSchemaGuard()
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        SettingsManager.RestoreSnapshot(new AppSettings());
        byte[] original = File.ReadAllBytes(settingsPath);
        foreach (int version in new[] { 2, 0, -1 })
        {
            SettingsManager.Current.SchemaVersion = version;
            AssertThrows<JsonException>(SettingsManager.SaveSettings, "save cannot publish a schema the build does not support");
            Assert(File.ReadAllBytes(settingsPath).SequenceEqual(original), "an invalid in-memory version cannot replace valid persisted settings");
        }
        SettingsManager.Current.SchemaVersion = AppSettings.CurrentSchemaVersion;
        SettingsManager.SaveSettings();
        SettingsManager.LoadSettings();
        Assert(SettingsManager.LoadError is null, "restoring a supported schema re-enables normal persistence");
    }
}
