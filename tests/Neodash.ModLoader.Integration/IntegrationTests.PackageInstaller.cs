using static Neodash.ModLoader.Integration.TestFixtures;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static void InstallRestoreAndConfiguration(string scratch)
    {
        var paths = NewGame(scratch, "install");
        string old = CreateLiveMod(paths.Game, "ConfigMod", false);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(old)!)!, "user.ini"), "keep=true");
        File.WriteAllText(Path.Combine(paths.Game, "Mods", "mods.txt"), "ConfigMod : 1\nOther : 1\nKeybinds : 1\n");
        var result = ModPackageInstaller.InstallFolder(paths.Game, CreateFolderPackage(paths.Root, "ConfigMod", "Scripts/main.lua"), paths.Backups, ["ConfigMod"]);
        Assert(result.Success, "disabled update succeeds");
        Assert(Directory.Exists(Path.Combine(paths.Game, "DisabledMods", "ConfigMod")), "disabled state survives update");
        Assert(File.ReadAllText(Path.Combine(paths.Game, "DisabledMods", "ConfigMod", "user.ini")) == "keep=true", "user config survives absent package config");
        File.AppendAllText(Path.Combine(paths.Game, "Mods", "mods.txt"), "Later : 1\n");
        var restore = ModPackageInstaller.RestoreBackup(paths.Game, result.BackupPath!, paths.Backups);
        Assert(restore.Success && restore.Folders.SequenceEqual(["ConfigMod"]), "restore reports actual restored folders");
        Assert(File.ReadAllText(Path.Combine(paths.Game, "Mods", "mods.txt")).Contains("Later : 1"), "restore preserves unrelated later mods.txt entries");
    }

    private static void RenamedPackageAndRestore(string scratch)
    {
        var paths = NewGame(scratch, "rename");
        CreateLiveMod(paths.Game, "OldOne", true);
        CreateLiveMod(paths.Game, "OldTwo", false);
        string release = Path.Combine(paths.Root, "release");
        CreateFolderPackage(release, "NewOne", "Scripts/main.lua");
        CreateFolderPackage(release, "NewTwo", "dlls/new.dll");
        var result = ModPackageInstaller.InstallFolder(paths.Game, release, paths.Backups, ["OldOne", "OldTwo"]);
        Assert(result.Success && result.Folders.Count == 2 && result.ReplacedFolders.Count == 4, "renamed multi-folder release is tracked");
        Assert(!ModManager.IsModInstalled(paths.Game, "OldOne") && !ModManager.IsModInstalled(paths.Game, "OldTwo"), "obsolete folders are deleted");
        var restore = ModPackageInstaller.RestoreBackup(paths.Game, result.BackupPath!, paths.Backups);
        Assert(restore.Success && restore.Folders.Count == 2 && ModManager.IsModInstalled(paths.Game, "OldOne") && ModManager.IsModInstalled(paths.Game, "OldTwo"), "restore returns prior multi-folder package");

        var renamed = NewGame(scratch, "rename-disabled");
        string oldFile = CreateLiveMod(renamed.Game, "OldDisabled", false);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(oldFile)!)!, "user.json"), "{\"keep\":true}");
        var renamedResult = ModPackageInstaller.InstallFolder(renamed.Game, CreateFolderPackage(renamed.Root, "NewDisabled", "Scripts/main.lua"), renamed.Backups, ["OldDisabled"]);
        Assert(renamedResult.Success && Directory.Exists(Path.Combine(renamed.Game, "DisabledMods", "NewDisabled")), "unambiguous renamed mod retains disabled state");
        Assert(File.ReadAllText(Path.Combine(renamed.Game, "DisabledMods", "NewDisabled", "user.json")) == "{\"keep\":true}", "unambiguous renamed mod preserves configuration");
    }

    private static void ZipLayoutsAndUnsupportedArchive(string scratch)
    {
        var paths = NewGame(scratch, "zips");
        string wrapped = Path.Combine(paths.Root, "wrapped.zip");
        CreateZip(wrapped, ("release/Mods/Wrapped/Scripts/main.lua", "ok"));
        Assert(ModPackageInstaller.InstallZip(paths.Game, wrapped, paths.Backups).Success && ModManager.IsModInstalled(paths.Game, "Wrapped"), "wrapper ZIP installs");
        string direct = Path.Combine(paths.Root, "direct.zip");
        CreateZip(direct, ("Scripts/main.lua", "ok"), ("enabled.txt", ""));
        var directResult = ModPackageInstaller.InstallZip(paths.Game, direct, paths.Backups, directFolderName: "DirectMod");
        Assert(directResult.Success, "direct root ZIP installs with chosen name: " + string.Join("; ", directResult.Errors));
        Assert(ModManager.IsModInstalled(paths.Game, "DirectMod"), "direct root ZIP creates the chosen mod folder");
        string unsupported = Path.Combine(paths.Root, "unsupported.zip");
        CreateZip(unsupported, ("readme.txt", "no mod"));
        Assert(!ModPackageInstaller.InstallZip(paths.Game, unsupported, paths.Backups).Success, "unsupported archive is rejected");
        string duplicate = Path.Combine(paths.Root, "duplicate.zip");
        CreateZip(duplicate, ("Dup/Scripts/main.lua", "first"), ("dup\\Scripts\\main.lua", "second"));
        Assert(!ModPackageInstaller.InstallZip(paths.Game, duplicate, paths.Backups).Success && !ModManager.IsModInstalled(paths.Game, "Dup"), "case and slash duplicate archive paths are rejected");
        string sentinel = Path.Combine(paths.Root, "sentinel.txt");
        File.WriteAllText(sentinel, "unchanged");
        string traversal = Path.Combine(paths.Root, "config-traversal.zip");
        CreateZip(traversal, ("Config/Scripts/main.lua", "ok"), ("Config/../../sentinel.txt", "overwrite"));
        Assert(!ModPackageInstaller.InstallZip(paths.Game, traversal, paths.Backups).Success && File.ReadAllText(sentinel) == "unchanged", "configuration traversal ZIP is rejected without changing outside file");
    }

    private static void PermanentUninstallAndLockedRecovery(string scratch)
    {
        var paths = NewGame(scratch, "uninstall");
        CreateLiveMod(paths.Game, "DeleteMe", true);
        CreateLiveMod(paths.Game, "DeleteMe", false);
        File.WriteAllText(Path.Combine(paths.Game, "Mods", "mods.txt"), "DeleteMe : 1\nOther : 1\n");
        ModPackageInstaller.Uninstall(paths.Game, ["DeleteMe"]);
        Assert(!ModManager.IsModInstalled(paths.Game, "DeleteMe"), "uninstall removes enabled and disabled copies");
        Assert(!File.ReadAllText(Path.Combine(paths.Game, "Mods", "mods.txt")).Contains("DeleteMe", StringComparison.OrdinalIgnoreCase), "uninstall removes mods.txt entry");
        string locked = CreateLiveMod(paths.Game, "Locked", false);
        string backup = ModPackageInstaller.CreateBackup(paths.Game, ["Locked"], paths.Backups);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false;
            try { ModPackageInstaller.Uninstall(paths.Game, ["Locked"]); } catch (IOException) { failed = true; }
            Assert(failed, "locked uninstall signals its failure");
        }
        Assert(ModPackageInstaller.RestoreBackup(paths.Game, backup, paths.Backups).Success && ModManager.IsModInstalled(paths.Game, "Locked"), "public backup restores locked uninstall state");
    }

    private static void UnsafeSnapshotsAndFolders(string scratch)
    {
        var one = NewGame(scratch, "one");
        var two = NewGame(scratch, "two");
        CreateLiveMod(one.Game, "First", true);
        string backup = ModPackageInstaller.CreateBackup(one.Game, ["First"], one.Backups);
        Assert(!ModPackageInstaller.RestoreBackup(two.Game, backup, one.Backups).Success, "foreign-game snapshot is rejected");
        Assert(!ModPackageInstaller.InstallFolder(one.Game, CreateFolderPackage(one.Root, "CON", "Scripts/main.lua"), one.Backups).Success, "invalid Windows device name is rejected");
        Assert(!ModPackageInstaller.InstallFolder(one.Game, CreateFolderPackage(one.Root, "Keybinds", "Scripts/main.lua"), one.Backups).Success, "reserved UE4SS folder is rejected");
        CreateLiveMod(one.Game, "Victim", true);
        string malicious = Path.Combine(one.Backups, "malicious");
        Directory.CreateDirectory(malicious);
        string escaped = one.Game.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(malicious, "snapshot.json"), $$"""{"GamePath":"{{escaped}}","AffectedFolders":["Victim"],"Folders":[{"Name":"Victim","Enabled":true,"StorageName":"../outside"}]}""");
        Assert(!ModPackageInstaller.RestoreBackup(one.Game, malicious, one.Backups).Success && ModManager.IsModInstalled(one.Game, "Victim"), "snapshot traversal cannot delete live files");
        var linked = NewGame(scratch, "linked");
        string external = Path.Combine(linked.Root, "external");
        string mods = Path.Combine(linked.Game, "Mods");
        Directory.CreateDirectory(external); Directory.Delete(mods);
        try
        {
            CreateJunction(mods, external);
            FileSystemInfo? resolved = new DirectoryInfo(mods).ResolveLinkTarget(returnFinalTarget: true);
            Assert(resolved is not null && string.Equals(resolved.FullName, new DirectoryInfo(external).FullName, StringComparison.OrdinalIgnoreCase), "junction resolves to the fixture external target");
            Assert(!ModPackageInstaller.InstallFolder(linked.Game, CreateFolderPackage(linked.Root, "Safe", "Scripts/main.lua"), linked.Backups).Success, "linked Mods parent is rejected");
        }
        finally
        {
            if (Directory.Exists(mods) && (File.GetAttributes(mods) & FileAttributes.ReparsePoint) != 0) Directory.Delete(mods);
            if (!Directory.Exists(mods)) Directory.CreateDirectory(mods);
        }
    }

    private static void CancelledInstall(string scratch)
    {
        var paths = NewGame(scratch, "cancelled");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ModInstallResult result = ModPackageInstaller.InstallFolder(paths.Game, CreateFolderPackage(paths.Root, "Cancelled", "Scripts/main.lua"), paths.Backups, cancellation.Token);
        Assert(!result.Success && !ModManager.IsModInstalled(paths.Game, "Cancelled"), "cancelled install leaves no live mod folder");
    }

    private static void CancelledUpdatePreservesExistingMod(string scratch)
    {
        var paths = NewGame(scratch, "cancelled-update");
        string existing = CreateLiveMod(paths.Game, "Existing", true);
        File.WriteAllText(existing, "old-content");
        string package = CreateFolderPackage(paths.Root, "Existing", "Scripts/main.lua");
        File.WriteAllText(Path.Combine(package, "Scripts", "main.lua"), "new-content");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ModInstallResult result = ModPackageInstaller.InstallFolder(paths.Game, package, paths.Backups, ["Existing"], cancellation.Token);
        Assert(!result.Success && File.ReadAllText(existing) == "old-content", "a cancelled update leaves the existing live mod unchanged");
        Assert(!Directory.Exists(paths.Backups) || !Directory.EnumerateFileSystemEntries(paths.Backups).Any(), "a cancelled update leaves no complete or partial recovery snapshot");
    }
}
