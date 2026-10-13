using static Neodash.ModLoader.Integration.TestFixtures;
using Neodash_Mod_Loader;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static void FileMoveRetryBehavior()
    {
        foreach (Exception transient in new Exception[]
        {
            new IOException("Access denied", unchecked((int)0x80070005)),
            new IOException("Sharing violation", unchecked((int)0x80070020)),
            new IOException("Lock violation", unchecked((int)0x80070021)),
            new UnauthorizedAccessException("Access denied")
        })
        {
            int attempts = 0;
            List<TimeSpan> delays = [];
            FileSystemRetry.Move(() =>
            {
                attempts++;
                if (attempts < 3) throw transient;
            }, delays.Add);
            Assert(attempts == 3 && delays.SequenceEqual(new[] { TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40) }), "temporary Windows move errors recover after bounded increasing delays");
        }

        var persistent = new IOException("Persistent sharing violation", unchecked((int)0x80070020));
        int failedAttempts = 0;
        List<TimeSpan> failureDelays = [];
        IOException exhausted = AssertThrowsAndReturn<IOException>(() => FileSystemRetry.Move(() =>
        {
            failedAttempts++;
            throw persistent;
        }, failureDelays.Add), "a persistent move failure is surfaced after four attempts");
        Assert(ReferenceEquals(exhausted, persistent) && failedAttempts == 4 && failureDelays.SequenceEqual(new[] { TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(60) }), "exhaustion preserves the original error and stops without a fourth delay");

        foreach (Exception failure in new Exception[]
        {
            new IOException("Already exists", unchecked((int)0x800700B7)),
            new DirectoryNotFoundException("Missing source"),
            new ArgumentException("Invalid path"),
            new OperationCanceledException("Cancelled")
        })
        {
            int attempts = 0;
            List<TimeSpan> delays = [];
            Exception reported = AssertThrowsAndReturn<Exception>(() => FileSystemRetry.Move(() =>
            {
                attempts++;
                throw failure;
            }, delays.Add), "non-transient move failures are surfaced immediately");
            Assert(ReferenceEquals(reported, failure) && attempts == 1 && delays.Count == 0, "unrelated errors and cancellation are never retried or replaced");
        }

        int successfulAttempts = 0;
        FileSystemRetry.Move(() => successfulAttempts++, _ => throw new InvalidOperationException("An immediately successful move must not delay."));
        Assert(successfulAttempts == 1, "an immediately successful move executes once without delay");
    }

    private static void ModManagerBehavior(string scratch)
    {
        var paths = NewGame(scratch, "manager");
        File.WriteAllText(Path.Combine(paths.Game, "Mods", "mods.txt"), "Keybinds : 1\n");
        CreateLiveMod(paths.Game, "Toggle", false);
        ModManager.SetModEnabled(paths.Game, "Toggle", true);
        Assert(ModManager.GetMods(paths.Game).Single(mod => mod.Name == "Toggle").IsEnabled, "enable moves disabled mod to Mods");
        Assert(File.ReadAllText(Path.Combine(paths.Game, "Mods", "mods.txt")).Contains("Toggle : 1"), "enable writes mods.txt entry");
        ModManager.SetModEnabled(paths.Game, "Toggle", false);
        Assert(!ModManager.GetMods(paths.Game).Single(mod => mod.Name == "Toggle").IsEnabled, "disable moves mod to DisabledMods");
        CreateLiveMod(paths.Game, "Keybinds", true);
        CreateLiveMod(paths.Game, "Duplicate", true);
        CreateLiveMod(paths.Game, "Duplicate", false);
        var listed = ModManager.GetMods(paths.Game);
        Assert(!listed.Any(mod => mod.Name == "Keybinds") && listed.Single(mod => mod.Name == "Duplicate").IsEnabled, "reserved folders are excluded and enabled duplicate wins");

        var migration = NewGame(scratch, "manager-migration");
        CreateLiveMod(migration.Game, "LegacyDisabled", true);
        File.WriteAllText(Path.Combine(migration.Game, "Mods", "mods.txt"), "LegacyDisabled : 0\n");
        bool migrated = ModManager.MigrateModsTxtDisabledMods(migration.Game);
        Assert(migrated && Directory.Exists(ModManager.GetModPath(migration.Game, "LegacyDisabled", false)), $"mods.txt migration moves disabled legacy mods (migrated={migrated}, enabled={Directory.Exists(ModManager.GetModPath(migration.Game, "LegacyDisabled", true))})");

        CreateLiveMod(paths.Game, "Rollback", true);
        CreateLiveMod(paths.Game, "Conflict", true);
        CreateLiveMod(paths.Game, "Conflict", false);
        List<string> errors = ModManager.ApplyEnabledMods(paths.Game, []);
        Assert(errors.Count > 0 && Directory.Exists(ModManager.GetModPath(paths.Game, "Rollback", true)), "profile apply rolls back moves after a destination conflict");
    }

    private static void GameProcessMatching(string scratch)
    {
        var selected = NewGame(scratch, "runtime-selected");
        var other = NewGame(scratch, "runtime-other");
        string selectedExe = Path.Combine(selected.Game, "Runway-Win64-Shipping.exe");
        string otherExe = Path.Combine(other.Game, "Runway-Win64-Shipping.exe");
        File.WriteAllText(selectedExe, string.Empty);
        File.WriteAllText(otherExe, string.Empty);
        Assert(GameProcessMatcher.MatchesSelectedGame(selected.Game, selectedExe), "selected game process blocks writes");
        Assert(!GameProcessMatcher.MatchesSelectedGame(selected.Game, otherExe), "another Neodash installation does not block the selected game");
        Assert(GameProcessMatcher.MatchesSelectedGame(selected.Game, null), "an unreadable process path remains conservatively blocked");
    }
}
