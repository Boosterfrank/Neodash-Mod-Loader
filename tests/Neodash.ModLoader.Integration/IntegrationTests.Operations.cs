using static Neodash.ModLoader.Integration.TestFixtures;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static void OperationFailureSemantics()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        var recoverable = new ModInstallResult { Errors = ["cancelled", "rollback failed"], BackupPath = "recovery-snapshot" };
        IOException error = AssertThrowsAndReturn<IOException>(() => ModOperations.EnsureSuccess(recoverable, cancellation.Token), "multiple operation errors must be surfaced as IO failure");
        Assert(error.Message.Contains("Recovery backup: recovery-snapshot"), "recoverable cancellation keeps recovery backup path");
        AssertThrows<OperationCanceledException>(() => ModOperations.EnsureSuccess(new ModInstallResult { Errors = ["cancelled"] }, cancellation.Token), "single cancellation stays cancellation");
    }

    private static async Task LateGameStartAbortsMutations(string scratch)
    {
        var install = NewGame(scratch, "late-start-install");
        string installScript = CreateLiveMod(install.Game, "RaceInstall", true);
        await File.WriteAllTextAsync(installScript, "old-install");
        string installPackage = CreateFolderPackage(install.Root, "RaceInstall", "Scripts/main.lua");
        await File.WriteAllTextAsync(Path.Combine(installPackage, "Scripts", "main.lua"), "new-install");
        int installSnapshots = SnapshotCount(install.Backups);
        GameRuntime.RunningProbe = path => ModOperations.SameGame(path, install.Game) && SnapshotCount(install.Backups) > installSnapshots;
        ModInstallResult installResult;
        try { installResult = ModPackageInstaller.InstallFolder(install.Game, installPackage, install.Backups, ["RaceInstall"]); }
        finally { GameRuntime.RunningProbe = null; }
        Assert(!installResult.Success && await File.ReadAllTextAsync(installScript) == "old-install", "a game that starts after install snapshot creation prevents file replacement");
        Assert(SnapshotCount(install.Backups) == installSnapshots, "a late-start install failure removes its transient snapshot");

        var restore = NewGame(scratch, "late-start-restore");
        string firstPackage = CreateFolderPackage(restore.Root, "RaceRestore", "Scripts/main.lua");
        await File.WriteAllTextAsync(Path.Combine(firstPackage, "Scripts", "main.lua"), "old-restore");
        ModInstallResult first = ModPackageInstaller.InstallFolder(restore.Game, firstPackage, restore.Backups);
        Assert(first.Success, "restore race fixture initial install succeeds");
        string secondPackage = Path.Combine(restore.Root, "replacement");
        string replacementPackage = CreateFolderPackage(secondPackage, "RaceRestore", "Scripts/main.lua");
        await File.WriteAllTextAsync(Path.Combine(replacementPackage, "Scripts", "main.lua"), "new-restore");
        ModInstallResult update = ModPackageInstaller.InstallFolder(restore.Game, replacementPackage, restore.Backups, ["RaceRestore"]);
        Assert(update.Success && update.BackupPath is not null, "restore race fixture creates a historical snapshot");
        int restoreSnapshots = SnapshotCount(restore.Backups);
        GameRuntime.RunningProbe = path => ModOperations.SameGame(path, restore.Game) && SnapshotCount(restore.Backups) > restoreSnapshots;
        ModInstallResult restoreResult;
        try { restoreResult = ModPackageInstaller.RestoreBackup(restore.Game, update.BackupPath!, restore.Backups); }
        finally { GameRuntime.RunningProbe = null; }
        Assert(!restoreResult.Success && await File.ReadAllTextAsync(Path.Combine(restore.Game, "Mods", "RaceRestore", "Scripts", "main.lua")) == "new-restore", "a game that starts after restore snapshot creation prevents restore mutation");
        Assert(SnapshotCount(restore.Backups) == restoreSnapshots, "a late-start restore failure preserves only the existing historical snapshots");

        var uninstall = NewGame(scratch, "late-start-uninstall");
        await File.WriteAllTextAsync(Path.Combine(uninstall.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        SettingsManager.Current.GameDirectory = uninstall.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Late start uninstall fixture", uninstall.Game);
        string uninstallPackage = Path.Combine(uninstall.Root, "race-uninstall.zip");
        CreateZip(uninstallPackage, ("RaceUninstall/Scripts/main.lua", "keep"));
        await ModOperations.ImportZipAsync(uninstallPackage);
        string uninstallScript = Path.Combine(uninstall.Game, "Mods", "RaceUninstall", "Scripts", "main.lua");
        string settingsBefore = JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings);
        string[] profileBefore = [.. ProfileManager.ActiveProfile.EnabledMods];
        int uninstallSnapshots = SnapshotCount(AppStorage.BackupsPath);
        GameRuntime.RunningProbe = path => ModOperations.SameGame(path, uninstall.Game) && SnapshotCount(AppStorage.BackupsPath) > uninstallSnapshots;
        try { await AssertThrowsAsync<InvalidOperationException>(() => ModOperations.UninstallFoldersAsync(["RaceUninstall"]), "a game that starts after uninstall snapshot creation aborts deletion"); }
        finally { GameRuntime.RunningProbe = null; }
        Assert(await File.ReadAllTextAsync(uninstallScript) == "keep" && ModManager.IsModInstalled(uninstall.Game, "RaceUninstall"), "late-start uninstall leaves live files intact");
        Assert(JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings) == settingsBefore && ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(profileBefore), "late-start uninstall preserves installation metadata and profile state");
        Assert(SnapshotCount(AppStorage.BackupsPath) == uninstallSnapshots, "late-start uninstall removes its transient recovery snapshot");
    }

    private static async Task ServiceTransactions(string scratch)
    {
        var paths = NewGame(scratch, "services");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Mods", "mods.txt"), "Other : 1\nKeybinds : 1\n");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Service transaction fixture", paths.Game);

        string firstZip = Path.Combine(paths.Root, "service.zip");
        CreateZip(firstZip, ("ServiceMod/Scripts/main.lua", "first"));
        await ModOperations.ImportZipAsync(firstZip);
        Assert(ModManager.IsModInstalled(paths.Game, "ServiceMod"), "actual ImportZipAsync installs into selected game");
        Assert(SettingsManager.Current.Installations.Count == 1 && SettingsManager.Current.Installations[0].DownloadUrl is null, "local import saves one source-free record");
        Assert(ProfileManager.ActiveProfile.EnabledMods.Contains("ServiceMod", StringComparer.OrdinalIgnoreCase), "local import syncs active profile");

        await ModOperations.RestoreFolderAsync("ServiceMod");
        Assert(!ModManager.IsModInstalled(paths.Game, "ServiceMod") && SettingsManager.Current.Installations.Count == 0, "empty initial backup restore deletes fresh install and its record");

        await ModOperations.ImportZipAsync(firstZip);
        string overlapZip = Path.Combine(paths.Root, "service-overlap.zip");
        CreateZip(overlapZip, ("ServiceMod/Scripts/main.lua", "second"));
        await ModOperations.ImportZipAsync(overlapZip);
        Assert(SettingsManager.Current.Installations.Count == 1 && await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "ServiceMod", "Scripts", "main.lua")) == "second", "overlapping local import replaces files without duplicating ownership");
        string portableImport = Path.Combine(paths.Root, "inactive-portable-profile.json");
        await File.WriteAllTextAsync(portableImport, "{\"activeProfile\":\"Portable inactive fixture\",\"profiles\":[{\"name\":\"Portable inactive fixture\",\"enabledMods\":[\"ServiceMod\"]}]}");
        ModProfile inactivePortable = ProfileManager.ImportProfile(portableImport);
        Assert(inactivePortable.GamePath is null, "imported inactive profiles stay unbound and portable");
        await ModOperations.UninstallFolderAsync("ServiceMod");
        Assert(!ModManager.IsModInstalled(paths.Game, "ServiceMod") && SettingsManager.Current.GetInstalledModVersion("ServiceMod") is null, "actual uninstall deletes mod and version metadata");
        Assert(!ProfileManager.ActiveProfile.EnabledMods.Contains("ServiceMod", StringComparer.OrdinalIgnoreCase), "actual uninstall removes all active profile references");
        Assert(inactivePortable.EnabledMods.Contains("ServiceMod", StringComparer.OrdinalIgnoreCase), "scoped uninstall preserves an inactive unbound imported profile");

        string bundleZip = Path.Combine(paths.Root, "bundle.zip");
        CreateZip(bundleZip, ("BundleOne/Scripts/main.lua", "one"), ("BundleTwo/Scripts/main.lua", "two"));
        await ModOperations.ImportZipAsync(bundleZip);
        Assert(SettingsManager.Current.Installations.Single(record => record.FolderNames.Contains("BundleOne")).FolderNames.Count == 2, "multi-folder import creates one ownership record");
        await ModOperations.UninstallFoldersAsync(["BundleOne", "BundleTwo"]);
        Assert(!ModManager.IsModInstalled(paths.Game, "BundleOne") && !ModManager.IsModInstalled(paths.Game, "BundleTwo"), "multi-folder uninstall removes every folder");
        Assert(!SettingsManager.Current.Installations.Any(record => record.FolderNames.Contains("BundleOne") || record.FolderNames.Contains("BundleTwo")), "multi-folder uninstall removes metadata");
        Assert(!ProfileManager.ActiveProfile.EnabledMods.Contains("BundleOne") && !ProfileManager.ActiveProfile.EnabledMods.Contains("BundleTwo"), "multi-folder uninstall removes profile references");

        await UninstallCommitFailureRestoresEverything(paths);
        await RestoreCommitFailureRestoresEverything(paths);
        await StaleBackupsCannotOverwriteNewOwnership(scratch);
        await PartialUninstallInvalidatesHistoricalBackup(scratch);

        string oldFolder = CreateLiveMod(paths.Game, "FaultMod", true);
        await File.WriteAllTextAsync(oldFolder, "old");
        string faultZip = Path.Combine(paths.Root, "fault.zip");
        CreateZip(faultZip, ("FaultMod/Scripts/main.lua", "new"));
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        bool failed = false;
        try { await ModOperations.ImportZipAsync(faultZip); }
        catch (IOException) { failed = true; }
        finally { AppStorage.FailSettingsWriteNumber = null; }
        Assert(failed, "post-install settings write failure is surfaced");
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "FaultMod", "Scripts", "main.lua")) == "old", "post-install settings failure restores original files");
        Assert(SettingsManager.Current.Installations.All(record => !record.FolderNames.Contains("FaultMod", StringComparer.OrdinalIgnoreCase)), "post-install settings failure restores installation metadata");
    }

    private static async Task StaleBackupsCannotOverwriteNewOwnership(string scratch)
    {
        var conflict = NewGame(scratch, "cross-identity-overlap");
        await File.WriteAllTextAsync(Path.Combine(conflict.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        SettingsManager.Current.GameDirectory = conflict.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Cross identity fixture", conflict.Game);
        string original = Path.Combine(conflict.Root, "original.zip");
        string replacement = Path.Combine(conflict.Root, "replacement.zip");
        CreateZip(original, ("PackageA/Scripts/main.lua", "a-original"), ("PackageB/Scripts/main.lua", "b-original"));
        CreateZip(replacement, ("PackageB/Scripts/main.lua", "b-new-owner"));
        await ModOperations.ImportZipAsync(original);
        string beforeA = await File.ReadAllTextAsync(Path.Combine(conflict.Game, "Mods", "PackageA", "Scripts", "main.lua"));
        string beforeB = await File.ReadAllTextAsync(Path.Combine(conflict.Game, "Mods", "PackageB", "Scripts", "main.lua"));
        IOException crossIdentity = await AssertThrowsAsyncAndReturn<IOException>(() => ModOperations.ImportZipAsync(replacement), "a different package cannot claim a folder owned by another package");
        Assert(crossIdentity.Message.Contains("already owned by", StringComparison.OrdinalIgnoreCase), "cross-identity rejection explains the ownership conflict");
        Assert(await File.ReadAllTextAsync(Path.Combine(conflict.Game, "Mods", "PackageA", "Scripts", "main.lua")) == beforeA && await File.ReadAllTextAsync(Path.Combine(conflict.Game, "Mods", "PackageB", "Scripts", "main.lua")) == beforeB, "cross-identity rejection occurs before any package file is written");
        InstalledModRecord? originalOwner = ModOperations.FindRecordForFolder("PackageA");
        Assert(originalOwner is not null && originalOwner.FolderNames.Count == 2 && originalOwner.FolderNames.Contains("PackageA", StringComparer.OrdinalIgnoreCase) && originalOwner.FolderNames.Contains("PackageB", StringComparer.OrdinalIgnoreCase) && ModOperations.FindRecordForFolder("PackageB")?.Id == originalOwner.Id, "cross-identity rejection preserves the original single ownership record");

        var historical = NewGame(scratch, "historical-stale-backup");
        await File.WriteAllTextAsync(Path.Combine(historical.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        CreateLiveMod(historical.Game, "PackageA", true);
        CreateLiveMod(historical.Game, "PackageB", true);
        string backup = ModPackageInstaller.CreateBackup(historical.Game, ["PackageA", "PackageB"], AppStorage.BackupsPath);
        ModPackageInstaller.ModBackupInfo inspection = ModPackageInstaller.InspectBackup(historical.Game, backup, AppStorage.BackupsPath);
        Assert(inspection.AffectedFolders.Contains("PackageA", StringComparer.OrdinalIgnoreCase) && inspection.AffectedFolders.Contains("PackageB", StringComparer.OrdinalIgnoreCase), "historical backup declares every folder it can affect");
        SettingsManager.Current.GameDirectory = historical.Game;
        SettingsManager.Current.Installations.RemoveAll(record => ModOperations.SameGame(record.GamePath, historical.Game));
        SettingsManager.Current.Installations.Add(new InstalledModRecord { Id = "package-p", Name = "Package P", Version = "1", GamePath = historical.Game, FolderNames = ["PackageA"], BackupPath = backup });
        SettingsManager.Current.Installations.Add(new InstalledModRecord { Id = "package-q", Name = "Package Q", Version = "1", GamePath = historical.Game, FolderNames = ["PackageB"] });
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Historical backup fixture", historical.Game);
        beforeA = await File.ReadAllTextAsync(Path.Combine(historical.Game, "Mods", "PackageA", "Scripts", "main.lua"));
        beforeB = await File.ReadAllTextAsync(Path.Combine(historical.Game, "Mods", "PackageB", "Scripts", "main.lua"));
        InvalidOperationException staleRestore = await AssertThrowsAsyncAndReturn<InvalidOperationException>(() => ModOperations.RestoreFolderAsync("PackageA"), "a historical backup cannot restore through a different package owner");
        Assert(staleRestore.Message.Contains("now owned by Package Q", StringComparison.Ordinal), "historical restore rejection identifies the current conflicting owner");
        Assert(await File.ReadAllTextAsync(Path.Combine(historical.Game, "Mods", "PackageA", "Scripts", "main.lua")) == beforeA && await File.ReadAllTextAsync(Path.Combine(historical.Game, "Mods", "PackageB", "Scripts", "main.lua")) == beforeB, "historical restore rejection leaves both live folders unchanged");
        Assert(ModOperations.FindRecordForFolder("PackageA")?.FolderNames.SequenceEqual(["PackageA"]) == true && ModOperations.FindRecordForFolder("PackageB")?.FolderNames.SequenceEqual(["PackageB"]) == true, "historical restore rejection preserves distinct ownership metadata");
    }

    private static async Task PartialUninstallInvalidatesHistoricalBackup(string scratch)
    {
        var paths = NewGame(scratch, "partial-uninstall");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Partial uninstall fixture", paths.Game);
        string package = Path.Combine(paths.Root, "pair.zip");
        CreateZip(package, ("PairA/Scripts/main.lua", "a"), ("PairB/Scripts/main.lua", "b"));
        await ModOperations.ImportZipAsync(package);
        Assert(ModOperations.FindRecordForFolder("PairA")?.BackupPath is not null, "multi-folder installation starts with a restore snapshot");
        await ModOperations.UninstallFoldersAsync(["PairA"]);
        InstalledModRecord remaining = ModOperations.FindRecordForFolder("PairB")!;
        Assert(remaining.BackupPath is null, "partial uninstall invalidates a backup that could restore removed folders");
        await AssertThrowsAsync<InvalidOperationException>(() => ModOperations.RestoreFolderAsync("PairB"), "partial uninstall cannot restore through an invalidated historical backup");
        Assert(!ModManager.IsModInstalled(paths.Game, "PairA") && ModManager.IsModInstalled(paths.Game, "PairB"), "invalidated restore does not recreate a removed folder");
    }

    private static async Task UninstallCommitFailureRestoresEverything((string Root, string Game, string Backups) paths)
    {
        string zip = Path.Combine(paths.Root, "uninstall-fault.zip");
        CreateZip(zip, ("UninstallFault/Scripts/main.lua", "original"));
        await ModOperations.ImportZipAsync(zip);
        string script = Path.Combine(paths.Game, "Mods", "UninstallFault", "Scripts", "main.lua");
        string fileBefore = await File.ReadAllTextAsync(script);
        string modsBefore = await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "mods.txt"));
        string settingsBefore = JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings);
        string[] profileBefore = [.. ProfileManager.ActiveProfile.EnabledMods];
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        bool failed = false;
        try { await ModOperations.UninstallFoldersAsync(["UninstallFault"]); }
        catch (Exception) { failed = true; }
        finally { AppStorage.FailSettingsWriteNumber = null; }
        Assert(failed, "uninstall settings commit failure is surfaced");
        Assert(await File.ReadAllTextAsync(script) == fileBefore && await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "mods.txt")) == modsBefore, "uninstall commit failure restores live files and mods.txt");
        Assert(JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings) == settingsBefore, "uninstall commit failure restores installation records and version keys");
        Assert(ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(profileBefore), "uninstall commit failure preserves active profile enabled mods");
    }

    private static async Task RestoreCommitFailureRestoresEverything((string Root, string Game, string Backups) paths)
    {
        string first = Path.Combine(paths.Root, "restore-fault-first.zip");
        string second = Path.Combine(paths.Root, "restore-fault-second.zip");
        CreateZip(first, ("RestoreFault/Scripts/main.lua", "old"));
        CreateZip(second, ("RestoreFault/Scripts/main.lua", "new"));
        await ModOperations.ImportZipAsync(first);
        await ModOperations.ImportZipAsync(second);
        string script = Path.Combine(paths.Game, "Mods", "RestoreFault", "Scripts", "main.lua");
        string fileBefore = await File.ReadAllTextAsync(script);
        string modsBefore = await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "mods.txt"));
        string settingsBefore = JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings);
        string[] profileBefore = [.. ProfileManager.ActiveProfile.EnabledMods];
        Assert(ModOperations.FindRecordForFolder("RestoreFault")?.BackupPath is not null, "restore failure fixture has update backup");
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        bool failed = false;
        try { await ModOperations.RestoreFolderAsync("RestoreFault"); }
        catch (Exception) { failed = true; }
        finally { AppStorage.FailSettingsWriteNumber = null; }
        Assert(failed, "restore settings commit failure is surfaced");
        Assert(await File.ReadAllTextAsync(script) == fileBefore && await File.ReadAllTextAsync(Path.Combine(paths.Game, "Mods", "mods.txt")) == modsBefore, "restore commit failure restores current live files and mods.txt");
        Assert(JsonSerializer.Serialize(SettingsManager.Current, AppJsonContext.Default.AppSettings) == settingsBefore, "restore commit failure restores installation records and version keys");
        Assert(ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(profileBefore), "restore commit failure preserves active profile enabled mods");
    }
}
