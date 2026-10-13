using static Neodash.ModLoader.Integration.TestFixtures;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static async Task<Dictionary<string, string>> SeedOldUe4SsRuntimeAsync(string game)
    {
        Dictionary<string, string> content = new()
        {
            ["UE4SS.dll"] = "old-runtime", ["dwmapi.dll"] = "old-proxy",
            ["obsolete-loader.dll"] = "old-root-helper",
            [Path.Combine("ue4ss", "obsolete-loader.dll")] = "old-nested-helper",
            [Path.Combine("ue4ss", "UE4SS.dll")] = "old-nested-runtime",
            ["custom.dll"] = "untracked-custom",
            [Path.Combine("Mods", "RunwayMP", "dlls", "main.dll")] = "user-native-mod",
            ["UE4SS-settings.ini"] = "user-settings"
        };
        foreach (var item in content)
        {
            string file = Path.Combine(game, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, item.Value);
        }
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord
        {
            GamePath = game, Version = "old", Files = content.Keys.Where(key => key != "custom.dll").Concat(["UE4SS.dll", "missing-loader.dll"]).ToList()
        });
        return content;
    }

    private static async Task Ue4SsUpdateRemovesOnlyObsoleteOwnedRuntimeFilesCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-cleanup");
        await SeedOldUe4SsRuntimeAsync(paths.Game);
        var other = NewGame(scratch, "other-game");
        await File.WriteAllTextAsync(Path.Combine(other.Game, "obsolete-loader.dll"), "other-game-helper");
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord { GamePath = other.Game, Version = "other", Files = ["obsolete-loader.dll"] });

        await Ue4SsService.InstallArchiveAsync(paths.Game, CreateUe4SsArchive("ue4ss-cleanup.zip"));

        Assert(!File.Exists(Path.Combine(paths.Game, "obsolete-loader.dll")) && !File.Exists(Path.Combine(paths.Game, "ue4ss", "obsolete-loader.dll")) && !File.Exists(Path.Combine(paths.Game, "ue4ss", "UE4SS.dll")), "obsolete tracked runtime DLLs in both layouts are removed, including the old preferred runtime");
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll")) == "new-runtime" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll")) == "new-proxy", "runtime DLLs still present in the new package are replaced rather than deleted");
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "custom.dll")) == "untracked-custom" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "RunwayMP", "dlls", "main.dll")) == "user-native-mod" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS-settings.ini")) == "user-settings", "cleanup preserves unknown DLLs, DLL mods and custom configuration");
        Assert(await File.ReadAllTextAsync(Path.Combine(other.Game, "obsolete-loader.dll")) == "other-game-helper" && SettingsManager.Current.Ue4ssInstallations.Single(record => ModOperations.SameGame(record.GamePath, other.Game)).Version == "other", "cleanup is scoped to the updated game and leaves its neighbour's ownership metadata intact");
        Assert(new HashSet<string>(SettingsManager.Current.Ue4ssInstallations.Single(record => ModOperations.SameGame(record.GamePath, paths.Game)).Files, StringComparer.OrdinalIgnoreCase).SetEquals(Ue4SsPackageFiles()), "successful cleanup commits only the new package file inventory");

        await Ue4SsService.InstallArchiveAsync(paths.Game, CreateUe4SsArchive("ue4ss-repeat.zip"));
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "custom.dll")) == "untracked-custom" && !Directory.EnumerateDirectories(AppStorage.DownloadsPath).Any(), "repeated repair is idempotent and cleans its temporary backups");
    }

    private static async Task Ue4SsObsoleteRuntimeCleanupRollsBackOnCommitFailureCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-cleanup-rollback");
        var original = await SeedOldUe4SsRuntimeAsync(paths.Game);
        int failingWrite = AppStorage.SettingsWriteCount + 1;
        AppStorage.FailSettingsWriteNumber = failingWrite;
        try { await AssertThrowsAsync<IOException>(() => Ue4SsService.InstallArchiveAsync(paths.Game, CreateUe4SsArchive("ue4ss-cleanup-failure.zip")), "failed settings commit is surfaced after cleanup"); }
        finally { AppStorage.FailSettingsWriteNumber = null; }

        Assert(AppStorage.SettingsWriteCount == failingWrite, "rollback scenario reaches the injected settings commit failure");
        foreach (var item in original)
            Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, item.Key)) == item.Value, "failed update restores exact prior bytes for " + item.Key);
        Assert(SettingsManager.Current.Ue4ssInstallations.Single().Version == "old" && SettingsManager.Current.Ue4ssInstallations.Single().Files.Contains("obsolete-loader.dll"), "failed update restores old runtime ownership metadata");
        Assert(!File.Exists(Path.Combine(paths.Game, "Mods", "Keybinds", "Scripts", "main.lua")) && Directory.EnumerateDirectories(AppStorage.DownloadsPath).Count() == 1, "new package files are rolled back and the recovery backup is retained");
    }

    private static async Task Ue4SsCancelledCleanupRestoresDeletedDllsCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-cleanup-cancel");
        var original = await SeedOldUe4SsRuntimeAsync(paths.Game);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelAfterCleanupProgress(cancellation, paths.Game);
        await AssertThrowsAsync<OperationCanceledException>(() => Ue4SsService.InstallArchiveAsync(paths.Game, CreateUe4SsArchive("ue4ss-cleanup-cancel.zip"), progress, cancellation.Token), "cancellation after obsolete DLL removal rolls back before settings commit");
        Assert(progress.ReachedFinalization, "cancellation scenario reaches the point after obsolete DLL removal");
        Assert(progress.ObservedCleanup, "cancellation is injected after new runtime bytes were copied and obsolete root/nested DLLs were deleted");
        foreach (var item in original)
            Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, item.Key)) == item.Value, "cancelled update restores exact prior runtime and user bytes for " + item.Key);
        Assert(SettingsManager.Current.Ue4ssInstallations.Single().Version == "old" && !File.Exists(Path.Combine(paths.Game, "Mods", "Keybinds", "Scripts", "main.lua")), "cancelled update preserves old metadata and removes new package files");
    }

    private sealed class CancelAfterCleanupProgress(CancellationTokenSource cancellation, string gamePath) : IProgress<OperationProgress>
    {
        public bool ReachedFinalization { get; private set; }
        public bool ObservedCleanup { get; private set; }

        public void Report(OperationProgress value)
        {
            if (value.Message != "Finalizing UE4SS installation…") return;
            ReachedFinalization = true;
            ObservedCleanup = !File.Exists(Path.Combine(gamePath, "obsolete-loader.dll"))
                && !File.Exists(Path.Combine(gamePath, "ue4ss", "UE4SS.dll"))
                && File.ReadAllText(Path.Combine(gamePath, "UE4SS.dll")) == "new-runtime";
            cancellation.Cancel();
        }
    }

    private static async Task Ue4SsObsoleteRuntimeCleanupRejectsUnsafeOrLockedFilesCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-cleanup-lock");
        var original = await SeedOldUe4SsRuntimeAsync(paths.Game);
        using (var locked = new FileStream(Path.Combine(paths.Game, "obsolete-loader.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            await AssertThrowsAsync<IOException>(() => Ue4SsService.InstallArchiveAsync(paths.Game, CreateUe4SsArchive("ue4ss-locked.zip")), "locked obsolete DLL prevents an update from committing");
        foreach (var item in original)
            Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, item.Key)) == item.Value, "locked DLL cleanup restores the other runtime files: " + item.Key);

        var unsafeGame = NewGame(scratch, "ue4ss-cleanup-link");
        string outside = Path.Combine(unsafeGame.Root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "UE4SS.dll"), "outside-sentinel");
        string junction = Path.Combine(unsafeGame.Game, "ue4ss");
        CreateJunction(junction, outside);
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord { GamePath = unsafeGame.Game, Files = [Path.Combine("ue4ss", "UE4SS.dll")] });
        try
        {
            await AssertThrowsAsync<IOException>(() => Ue4SsService.InstallArchiveAsync(unsafeGame.Game, CreateUe4SsArchive("ue4ss-linked.zip")), "obsolete DLL cleanup rejects linked folders before replacing runtime files");
            Assert(await File.ReadAllTextAsync(Path.Combine(outside, "UE4SS.dll")) == "outside-sentinel" && !File.Exists(Path.Combine(unsafeGame.Game, "UE4SS.dll")), "unsafe cleanup leaves outside and current runtime files untouched");
        }
        finally { Directory.Delete(junction); }
    }

    private static async Task Ue4SsUninstallRestoresDistinctDllPathsOnFailureCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-cleanup-uninstall");
        var original = await SeedOldUe4SsRuntimeAsync(paths.Game);
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        try { await AssertThrowsAsync<IOException>(() => Ue4SsService.UninstallAsync(), "uninstall metadata failure is surfaced"); }
        finally { AppStorage.FailSettingsWriteNumber = null; }
        foreach (var item in original)
            Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, item.Key)) == item.Value, "relative backup paths recover distinct DLLs sharing a basename: " + item.Key);

        await Ue4SsService.UninstallAsync();
        Assert(!File.Exists(Path.Combine(paths.Game, "UE4SS.dll")) && !File.Exists(Path.Combine(paths.Game, "ue4ss", "UE4SS.dll")) && !File.Exists(Path.Combine(paths.Game, "obsolete-loader.dll")) && !File.Exists(Path.Combine(paths.Game, "ue4ss", "obsolete-loader.dll")), "uninstall removes all owned runtime DLL paths");
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "custom.dll")) == "untracked-custom" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "RunwayMP", "dlls", "main.dll")) == "user-native-mod" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS-settings.ini")) == "user-settings", "runtime uninstall keeps unrelated DLLs, native mods and settings");
    }
}
