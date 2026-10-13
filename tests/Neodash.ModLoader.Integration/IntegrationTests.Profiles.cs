using static Neodash.ModLoader.Integration.TestFixtures;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static void ProfileBehavior(string scratch)
    {
        var first = ProfileManager.CreateProfile("  Workshop  ");
        Assert(ProfileManager.ActiveProfile == first && first.Name == "Workshop", "create profile trims and activates name");
        ProfileManager.RenameProfile(first, "Build");
        Assert(ProfileManager.ActiveProfile.Name == "Build", "rename updates active profile");
        AssertThrows<ArgumentException>(() => ProfileManager.CreateProfile("build"), "duplicate profile names are rejected");
        first.EnabledMods.AddRange(["Zebra", "Alpha"]);
        string export = Path.Combine(scratch, "profiles", "build.json");
        ProfileManager.ExportProfile(export);
        ModProfileData exported = JsonSerializer.Deserialize(File.ReadAllText(export), AppJsonContext.Default.ModProfileData)!;
        Assert(exported.ActiveProfile == "Build" && exported.Profiles.Count == 1 && exported.Profiles[0].Name == "Build" && exported.Profiles[0].EnabledMods.SequenceEqual(["Alpha", "Zebra"]), "profile export preserves active profile name and normalized enabled mods");
        ProfileManager.SetActiveProfile(ProfileManager.Profiles.First(profile => profile.Name == "Default"));
        ProfileManager.DeleteProfile(first);
        Assert(!ProfileManager.Profiles.Contains(first), "non-active profile deletion works");
        File.WriteAllText(export, """{"activeProfile":"Imported","profiles":[{"name":"Imported","enabledMods":[" Zebra ","zebra","Alpha"]}]}""");
        ModProfile imported = ProfileManager.ImportProfile(export);
        Assert(imported.EnabledMods.SequenceEqual(["Alpha", "Zebra"]), "profile import normalizes duplicate mod names");
        AssertThrows<InvalidOperationException>(() => ProfileManager.ImportProfile(export), "import rejects duplicate profile name");
        File.WriteAllText(export, "{ not json }");
        AssertThrows<InvalidOperationException>(() => ProfileManager.ImportProfile(export), "malformed profile export is rejected");
    }

    private static void ModStatePersistenceRollback(string scratch)
    {
        var paths = NewGame(scratch, "mod-state-rollback");
        File.WriteAllText(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        CreateLiveMod(paths.Game, "EnabledBefore", true);
        CreateLiveMod(paths.Game, "DisabledBefore", false);
        string modsTxt = Path.Combine(paths.Game, "Mods", "mods.txt");
        File.WriteAllText(modsTxt, "EnabledBefore : 1\nKeybinds : 1\n");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Mod state rollback fixture", paths.Game);
        string modsTxtBefore = File.ReadAllText(modsTxt);
        string profilesPath = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        using (new FileStream(profilesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertThrows<UnauthorizedAccessException>(() => ModStateTransactions.ApplyChanges(paths.Game, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["EnabledBefore"] = false,
                ["DisabledBefore"] = true
            }), "profile persistence failure is surfaced after state changes");
        }
        Assert(ModManager.IsModInstalled(paths.Game, "EnabledBefore") && !ModManager.GetMods(paths.Game).Single(mod => mod.Name == "DisabledBefore").IsEnabled, "profile persistence failure rolls both folder states back");
        Assert(File.ReadAllText(modsTxt) == modsTxtBefore, "profile persistence failure restores mods.txt exactly");
        Assert(ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(["EnabledBefore"]), "profile persistence failure restores the active profile state");

        ModStateTransactions.ApplyChanges(paths.Game, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnabledBefore"] = false,
            ["DisabledBefore"] = true
        });
        Assert(!ModManager.GetMods(paths.Game).Single(mod => mod.Name == "EnabledBefore").IsEnabled && ModManager.GetMods(paths.Game).Single(mod => mod.Name == "DisabledBefore").IsEnabled, "the same requested state changes succeed after the profile file is unlocked");
        Assert(ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(["DisabledBefore"]), "successful retry persists the requested enabled set");
    }

    private static void ProfileSyncPreservesUnavailableSelections(string scratch)
    {
        var paths = NewGame(scratch, "profile-sync");
        CreateLiveMod(paths.Game, "Enabled", true);
        CreateLiveMod(paths.Game, "Disabled", false);
        ModProfile active = ProfileManager.CreateProfile("Profile sync fixture", paths.Game);
        active.EnabledMods = ["Missing", "Disabled"];
        ProfileManager.SyncActiveProfile(paths.Game);
        Assert(active.EnabledMods.Contains("Missing", StringComparer.OrdinalIgnoreCase), "profile sync preserves a missing selected mod");
        Assert(!active.EnabledMods.Contains("Disabled", StringComparer.OrdinalIgnoreCase), "profile sync removes an installed disabled mod");
        Assert(active.EnabledMods.Contains("Enabled", StringComparer.OrdinalIgnoreCase), "profile sync adds an installed enabled mod");
        Assert(ModOperations.SameGame(active.GamePath!, paths.Game), "sync binds the active profile to the selected installation");

        var other = NewGame(scratch, "profile-other-game");
        string beforePath = active.GamePath!;
        string[] beforeMods = [.. active.EnabledMods];
        AssertThrows<InvalidOperationException>(() => ProfileManager.SetActiveProfile(active, other.Game), "a profile bound to another game cannot be selected for this installation");
        Assert(active.GamePath == beforePath && active.EnabledMods.SequenceEqual(beforeMods), "a rejected cross-game selection leaves the selected profile unchanged");

        string export = Path.Combine(paths.Root, "portable-profile.json");
        ProfileManager.ExportProfile(export);
        string json = File.ReadAllText(export);
        Assert(!json.Contains("GamePath", StringComparison.OrdinalIgnoreCase), "profile exports remain portable and omit the local game path");
    }

    private static void PackageFolderReconciliation(string scratch)
    {
        var game = NewGame(scratch, "profile-folder-reconcile");
        CreateLiveMod(game.Game, "Old", true);
        ModProfile active = ProfileManager.CreateProfile("Rename active fixture", game.Game);
        active.EnabledMods.Add("Missing");
        ModProfile sameGameInactive = ProfileManager.CreateProfile("Rename same-game fixture", game.Game);
        sameGameInactive.EnabledMods.Add("Missing");
        var otherGame = NewGame(scratch, "profile-folder-other-game");
        CreateLiveMod(otherGame.Game, "Old", true);
        ModProfile otherBound = ProfileManager.CreateProfile("Rename other-game fixture", otherGame.Game);
        ProfileManager.SetActiveProfile(active, game.Game);
        string portablePath = Path.Combine(game.Root, "portable-rename.json");
        File.WriteAllText(portablePath, "{\"activeProfile\":\"Portable rename fixture\",\"profiles\":[{\"name\":\"Portable rename fixture\",\"enabledMods\":[\"Old\",\"Missing\"]}]}");
        ModProfile portable = ProfileManager.ImportProfile(portablePath);

        CreateLiveMod(game.Game, "New", true);
        Directory.Delete(ModManager.GetModPath(game.Game, "Old", true), recursive: true);
        string profilesPath = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        using (new FileStream(profilesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertThrows<UnauthorizedAccessException>(() => ProfileManager.ReconcilePackageFoldersAndSync(game.Game, ["Old"], ["New"]), "profile write failure aborts folder-name reconciliation");
        }
        Assert(active.EnabledMods.Contains("Old") && sameGameInactive.EnabledMods.Contains("Old") && !active.EnabledMods.Contains("New") && !sameGameInactive.EnabledMods.Contains("New"), "failed rename reconciliation restores bound in-memory profile folder names");

        ProfileManager.ReconcilePackageFoldersAndSync(game.Game, ["Old"], ["New"]);
        Assert(active.EnabledMods.Contains("New") && sameGameInactive.EnabledMods.Contains("New") && !active.EnabledMods.Contains("Old") && !sameGameInactive.EnabledMods.Contains("Old"), "one-to-one package rename updates active and inactive profiles bound to this game");
        Assert(active.EnabledMods.Contains("Missing") && sameGameInactive.EnabledMods.Contains("Missing"), "package rename preserves unrelated missing selections in matching profiles");
        Assert(otherBound.EnabledMods.Contains("Old") && !otherBound.EnabledMods.Contains("New") && portable.EnabledMods.Contains("Old") && !portable.EnabledMods.Contains("New"), "package rename leaves other-game and portable profiles untouched");

        CreateLiveMod(game.Game, "Old", true);
        Directory.Delete(ModManager.GetModPath(game.Game, "New", true), recursive: true);
        ProfileManager.ReconcilePackageFoldersAndSync(game.Game, ["New"], ["Old"]);
        Assert(active.EnabledMods.Contains("Old") && sameGameInactive.EnabledMods.Contains("Old") && !active.EnabledMods.Contains("New") && !sameGameInactive.EnabledMods.Contains("New"), "restore reconciliation maps renamed folders back to their original profile names");

        active.EnabledMods.Add("Obsolete");
        sameGameInactive.EnabledMods.Add("Obsolete");
        ProfileManager.ReconcilePackageFoldersAndSync(game.Game, ["Old", "Obsolete"], ["Old"]);
        Assert(!active.EnabledMods.Contains("Obsolete") && !sameGameInactive.EnabledMods.Contains("Obsolete") && active.EnabledMods.Contains("Old") && sameGameInactive.EnabledMods.Contains("Old"), "multi-folder update removes only explicitly obsolete names from matching profiles");
    }
}
