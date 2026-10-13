using static Neodash.ModLoader.Integration.TestFixtures;
using static Neodash.ModLoader.Integration.IntegrationTestHost;
using System.Text;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static Task RunCorruptProfileChildAsync()
    {
        int result = CheckCorruptProfileInChildProcess();
        Assert(result == 0, "semantic profile corruption must be rejected without rewriting the original file");
        return Task.CompletedTask;
    }

    private static async Task LegacySettingsMigrationCompatibility(string scratch)
    {
        var paths = NewGame(scratch, "legacy-migration");
        CreateLiveMod(paths.Game, "LegacyDisabled", true);
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Mods", "mods.txt"), "LegacyDisabled : 0\n");
        string storage = Path.Combine(scratch, "legacy-migration-storage");
        Directory.CreateDirectory(storage);
        string escapedGame = paths.Game.Replace("\\", "\\\\");
        await File.WriteAllTextAsync(Path.Combine(storage, "settings.json"), "{\"IsSetupComplete\":true,\"AppTheme\":\"Light\",\"ModsTxtMigrated\":false,\"GameDirectory\":\"" + escapedGame + "\",\"InstalledMod_Legacy\":\"1.2.3\",\"UnknownFlag\":{\"keep\":true}}");
        await File.WriteAllTextAsync(Path.Combine(storage, "profiles.json"), """{"activeProfile":"Legacy","profiles":[{"name":"Legacy","enabledMods":["LegacyDisabled","Missing"]}]}""");
        await RunFilteredChildAsync(nameof(LegacySettingsMigrationKeepsProfilesAndSettingsCompatible), start =>
        {
            start.Environment["NEODASH_TEST_STORAGE"] = storage;
            start.Environment["NEODASH_LEGACY_MIGRATION_CHILD"] = "1";
            start.Environment["NEODASH_LEGACY_GAME"] = paths.Game;
        });
    }

    private static Task RunLegacyMigrationChildAsync()
    {
        string game = Environment.GetEnvironmentVariable("NEODASH_LEGACY_GAME") ?? throw new InvalidOperationException("Legacy migration fixture game path was not configured.");
        string profilesPath = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        SettingsManager.LoadSettings();
        _ = ProfileManager.ActiveProfile;
        using (new FileStream(profilesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertThrows<UnauthorizedAccessException>(() => SettingsMigration.MigrateLegacyModState(game), "locked legacy profile data blocks migration persistence");
            Assert(!SettingsManager.Current.ModsTxtMigrated && Directory.Exists(ModManager.GetModPath(game, "LegacyDisabled", false)), "failed legacy profile save leaves migration flag false after folder migration");
        }

        Assert(SettingsMigration.MigrateLegacyModState(game), "legacy migration retries after the profile file is unlocked");
        Assert(SettingsManager.Current.IsSetupComplete && SettingsManager.Current.AppTheme == "Light" && SettingsManager.Current.ModsTxtMigrated, "legacy migration preserves known settings and persists its completion flag");
        Assert(SettingsManager.Current.GetInstalledModVersion("Legacy") == "1.2.3" && SettingsManager.Current.AdditionalSettings.ContainsKey("UnknownFlag"), "legacy migration preserves legacy versions and unknown JSON data");
        Assert(ProfileManager.ActiveProfile.GamePath is not null && ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(["Missing"]), "legacy migration binds the active profile, removes disabled disk mods, and preserves missing selections");
        Assert(!ModManager.GetMods(game).Single(mod => mod.Name == "LegacyDisabled").IsEnabled, "legacy migration moves a zero-valued mods.txt mod into DisabledMods");
        Assert(ModManager.ApplyEnabledMods(game, ProfileManager.ActiveProfile.EnabledMods).Count == 0 && !ModManager.GetMods(game).Single(mod => mod.Name == "LegacyDisabled").IsEnabled, "reapplying the migrated profile does not re-enable the legacy disabled mod");
        SettingsManager.LoadSettings();
        Assert(SettingsManager.Current.ModsTxtMigrated && SettingsManager.Current.GetInstalledModVersion("Legacy") == "1.2.3" && SettingsManager.Current.AdditionalSettings.ContainsKey("UnknownFlag"), "saved legacy migration settings reload with known, legacy, and unknown data intact");
        return Task.CompletedTask;
    }

    private static void SettingsJsonCompatibilityCore()
    {
        const string json = """{"IsSetupComplete":true,"AppTheme":"Light","ModsTxtMigrated":false,"InstalledMod_Legacy":"1.2.3","InstalledMods":{"Modern":"2.0.0"},"Installations":[{"Id":"example","Name":"Example","Version":"3.0.0","GamePath":"C:\\Fake","FolderNames":["Example"]}],"UnknownFlag":{"preserved":true}}""";
        AppSettings settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)!;
        Assert(settings.IsSetupComplete && settings.AppTheme == "Light" && !settings.ModsTxtMigrated && settings.GetInstalledModVersion("Legacy") == "1.2.3" && settings.GetInstalledModVersion("Modern") == "2.0.0" && settings.AdditionalSettings.ContainsKey("UnknownFlag"), "known, legacy-version, and unknown settings values deserialize");
        AppSettings roundTripped = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings), AppJsonContext.Default.AppSettings)!;
        Assert(roundTripped.Installations.Count == 1 && roundTripped.Installations[0].FolderNames.Single() == "Example" && roundTripped.GetInstalledModVersion("Legacy") == "1.2.3" && roundTripped.AdditionalSettings.ContainsKey("UnknownFlag"), "records, legacy versions, and unknown settings round-trip");
        roundTripped.SetInstalledModVersion("Legacy", "1.2.4");
        Assert(roundTripped.GetInstalledModVersion("Legacy") == "1.2.4" && !roundTripped.AdditionalSettings.ContainsKey("InstalledMod_Legacy"), "writing a legacy version migrates its key into InstalledMods");
        roundTripped.RemoveInstalledModVersion("Legacy");
        Assert(roundTripped.GetInstalledModVersion("Legacy") is null && !roundTripped.InstalledMods.ContainsKey("Legacy"), "removing a legacy version clears both the migrated and legacy keys");
    }

    private static void SettingsCorruptionIsPreserved()
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        Directory.CreateDirectory(AppStorage.DirectoryPath);
        const string corrupt = "{ not valid json";
        File.WriteAllText(settingsPath, corrupt);
        SettingsManager.LoadSettings();
        Assert(SettingsManager.LoadError is not null && File.ReadAllText(settingsPath) == corrupt, "corrupt settings expose error and preserve original bytes");
        AssertThrows<InvalidOperationException>(SettingsManager.SaveSettings, "saving corrupt settings is blocked");
        File.WriteAllText(settingsPath, "{}");
        SettingsManager.LoadSettings();
        SettingsManager.SaveSettings();
        Assert(SettingsManager.LoadError is null, "successful reload re-enables settings writes");
    }

    private static async Task SemanticProfileCorruptionIsPreserved(string scratch)
    {
        string[] invalidDocuments =
        [
            "{\"activeProfile\":\"One\",\"profiles\":[{\"name\":\"One\",\"enabledMods\":[]},{\"name\":\"one\",\"enabledMods\":[]}]}",
            "{\"activeProfile\":\"One\",\"profiles\":[null]}",
            "{\"activeProfile\":\"Missing\",\"profiles\":[{\"name\":\"One\",\"enabledMods\":[]}]}",
            "{\"activeProfile\":\"Lost\",\"profiles\":[]}",
            "{\"activeProfile\":\"One\",\"profiles\":[{\"name\":\"One\",\"gamePath\":\"relative\",\"enabledMods\":[]}]}"
        ];
        for (int index = 0; index < invalidDocuments.Length; index++)
        {
            string storage = Path.Combine(scratch, "profile-corrupt-" + index);
            Directory.CreateDirectory(storage);
            byte[] original = Encoding.UTF8.GetBytes(invalidDocuments[index]);
            await File.WriteAllBytesAsync(Path.Combine(storage, "profiles.json"), original);
            await RunFilteredChildAsync(nameof(SemanticProfileCorruptionIsPreservedFact), start =>
            {
                start.Environment["NEODASH_TEST_STORAGE"] = storage;
                start.Environment[CorruptProfileChildVariable] = "1";
            });
            Assert((await File.ReadAllBytesAsync(Path.Combine(storage, "profiles.json"))).SequenceEqual(original), "semantic profile corruption preserves exact original bytes");
        }
    }

    private static int CheckCorruptProfileInChildProcess()
    {
        string path = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        byte[] original = File.ReadAllBytes(path);
        try
        {
            ProfileManager.EnsureWritable();
            Console.Error.WriteLine("Expected semantically corrupt profile data to be non-writable.");
            return 1;
        }
        catch (InvalidOperationException)
        {
            if (!File.ReadAllBytes(path).SequenceEqual(original))
            {
                Console.Error.WriteLine("Profile validation changed the source file.");
                return 2;
            }
            return 0;
        }
    }

    private static void SettingsNestedInvalidityIsPreserved()
    {
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        string[] invalidDocuments =
        [
            "{\"Installations\":[null]}",
            "{\"Installations\":[{\"Id\":\"x\",\"Name\":\"x\",\"Version\":\"1\",\"GamePath\":\"\",\"FolderNames\":null}]}",
            "{\"Installations\":[{\"Id\":\"x\",\"Name\":\"x\",\"Version\":\"1\",\"GamePath\":\"relative\",\"FolderNames\":[\"x\"]}]}"
        ];

        foreach (string invalid in invalidDocuments)
        {
            byte[] original = Encoding.UTF8.GetBytes(invalid);
            File.WriteAllBytes(settingsPath, original);
            SettingsManager.LoadSettings();
            Assert(SettingsManager.LoadError is not null, "invalid nested settings expose a load error");
            Assert(File.ReadAllBytes(settingsPath).SequenceEqual(original), "invalid nested settings preserve their exact original bytes");
            AssertThrows<InvalidOperationException>(SettingsManager.SaveSettings, "invalid nested settings block an overwrite");
        }

        File.WriteAllText(settingsPath, "{}");
        SettingsManager.LoadSettings();
        Assert(SettingsManager.LoadError is null, "a repaired settings document becomes writable again");
    }
}
