using static Neodash.ModLoader.Integration.TestFixtures;
using System.Text.Json;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{

    private static async Task Ue4SsArchiveInstallPreservesUserFilesAndTracksPackageCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-install");
        string settings = Path.Combine(paths.Game, "UE4SS-settings.ini");
        string modsTxt = Path.Combine(paths.Game, "Mods", "mods.txt");
        string keybind = Path.Combine(paths.Game, "Mods", "Keybinds", "Scripts", "main.lua");
        string helper = Path.Combine(paths.Game, "Mods", "shared", "UEHelpers", "UEHelpers.lua");
        string userMod = Path.Combine(paths.Game, "Mods", "CustomUser", "Scripts", "main.lua");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll"), "old-runtime");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll"), "old-proxy");
        Directory.CreateDirectory(Path.GetDirectoryName(keybind)!);
        Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
        Directory.CreateDirectory(Path.GetDirectoryName(userMod)!);
        await File.WriteAllTextAsync(settings, "custom-settings");
        await File.WriteAllTextAsync(modsTxt, "CustomUser : 1\n");
        await File.WriteAllTextAsync(keybind, "custom-keybind");
        await File.WriteAllTextAsync(helper, "custom-helper");
        await File.WriteAllTextAsync(userMod, "user-mod");
        string archive = CreateUe4SsArchive("ue4ss-success.zip");

        await Ue4SsService.InstallArchiveAsync(paths.Game, archive);

        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll")) == "new-runtime" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll")) == "new-proxy", "UE4SS archive install replaces only runtime DLLs");
        Assert(await File.ReadAllTextAsync(settings) == "custom-settings" && await File.ReadAllTextAsync(modsTxt) == "CustomUser : 1\n" && await File.ReadAllTextAsync(keybind) == "custom-keybind" && await File.ReadAllTextAsync(helper) == "custom-helper" && await File.ReadAllTextAsync(userMod) == "user-mod", "UE4SS archive install preserves custom settings, mods, support scripts, and user mod folders");
        Ue4SsInstallationRecord record = SettingsManager.Current.Ue4ssInstallations.Single(item => ModOperations.SameGame(item.GamePath, paths.Game));
        Assert(new HashSet<string>(record.Files, StringComparer.OrdinalIgnoreCase).SetEquals(Ue4SsPackageFiles()), "UE4SS archive install records every package file for this game");
        Assert(!File.Exists(archive) && !Directory.EnumerateDirectories(AppStorage.DownloadsPath).Any(), "successful UE4SS archive install deletes its archive and cleans staging and rollback directories");
    }

    private static async Task Ue4SsArchiveInstallRollsBackAfterSettingsFailureCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-install-failure");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll"), "old-runtime");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll"), "old-proxy");
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord { GamePath = paths.Game, Files = ["UE4SS.dll"], Version = "old" });
        string archive = CreateUe4SsArchive("ue4ss-failure.zip");
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 1;
        try
        {
            await AssertThrowsAsync<IOException>(() => Ue4SsService.InstallArchiveAsync(paths.Game, archive), "UE4SS settings commit failure is surfaced");
        }
        finally { AppStorage.FailSettingsWriteNumber = null; }

        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll")) == "old-runtime" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll")) == "old-proxy", "UE4SS settings failure restores replaced runtime DLLs");
        Assert(!File.Exists(Path.Combine(paths.Game, "UE4SS-settings.ini")) && !File.Exists(Path.Combine(paths.Game, "Mods", "Keybinds", "Scripts", "main.lua")), "UE4SS settings failure removes files introduced by the package");
        Assert(SettingsManager.Current.Ue4ssInstallations.Single().Version == "old" && !File.Exists(archive) && Directory.EnumerateDirectories(AppStorage.DownloadsPath).Count() == 1, "UE4SS settings failure restores metadata, deletes archive and staging, and retains its rollback directory");

        string invalid = Path.Combine(AppStorage.DownloadsPath, "ue4ss-invalid.zip");
        CreateZip(invalid, ("UE4SS.dll", "incomplete"));
        await AssertThrowsAsync<InvalidDataException>(() => Ue4SsService.InstallArchiveAsync(paths.Game, invalid), "incomplete UE4SS archive is rejected before mutation");
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll")) == "old-runtime" && !File.Exists(invalid), "invalid UE4SS archive leaves live files unchanged and is deleted");
    }

    private static async Task Ue4SsUninstallRestoresOnCommitFailureAndPreservesCustomFilesCore(string scratch)
    {
        var paths = NewGame(scratch, "ue4ss-uninstall");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll"), "runtime");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll"), "proxy");
        string settings = Path.Combine(paths.Game, "UE4SS-settings.ini");
        string modsTxt = Path.Combine(paths.Game, "Mods", "mods.txt");
        string helper = Path.Combine(paths.Game, "Mods", "shared", "UEHelpers", "UEHelpers.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(helper)!);
        await File.WriteAllTextAsync(settings, "custom-settings");
        await File.WriteAllTextAsync(modsTxt, "CustomUser : 1\n");
        await File.WriteAllTextAsync(helper, "custom-helper");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        await AssertThrowsAsync<InvalidOperationException>(() => Ue4SsService.UninstallAsync(), "untracked UE4SS installations refuse destructive uninstall");
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord { GamePath = paths.Game, Files = Ue4SsPackageFiles().ToList(), Version = "old" });
        SettingsManager.SaveSettings();

        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        try
        {
            await AssertThrowsAsync<IOException>(() => Ue4SsService.UninstallAsync(), "UE4SS uninstall settings commit failure is surfaced");
        }
        finally { AppStorage.FailSettingsWriteNumber = null; }
        Assert(await File.ReadAllTextAsync(Path.Combine(paths.Game, "UE4SS.dll")) == "runtime" && await File.ReadAllTextAsync(Path.Combine(paths.Game, "dwmapi.dll")) == "proxy" && SettingsManager.Current.Ue4ssInstallations.Count == 1, "UE4SS uninstall failure restores runtime bytes and installation metadata");

        await Ue4SsService.UninstallAsync();
        Assert(!File.Exists(Path.Combine(paths.Game, "UE4SS.dll")) && !File.Exists(Path.Combine(paths.Game, "dwmapi.dll")) && SettingsManager.Current.Ue4ssInstallations.Count == 0, "successful UE4SS uninstall removes only recorded runtime DLLs and metadata");
        Assert(await File.ReadAllTextAsync(settings) == "custom-settings" && await File.ReadAllTextAsync(modsTxt) == "CustomUser : 1\n" && await File.ReadAllTextAsync(helper) == "custom-helper", "successful UE4SS uninstall preserves configuration, mods list, and support scripts");
        Assert(Ue4SsService.GetStatus(string.Empty) == "Choose your Neodash folder first." && Ue4SsService.GetStatus(paths.Game) == "UE4SS is not installed.", "UE4SS status reports missing selection and removed runtime states");
    }

    private static string CreateUe4SsArchive(string name)
    {
        Directory.CreateDirectory(AppStorage.DownloadsPath);
        string archive = Path.Combine(AppStorage.DownloadsPath, name);
        CreateZip(archive,
            ("UE4SS.dll", "new-runtime"),
            ("dwmapi.dll", "new-proxy"),
            ("UE4SS-settings.ini", "package-settings"),
            ("Mods/mods.txt", "Package : 1\n"),
            ("Mods/Keybinds/Scripts/main.lua", "package-keybind"),
            ("Mods/shared/UEHelpers/UEHelpers.lua", "package-helper"));
        return archive;
    }

    private static IReadOnlyList<string> Ue4SsPackageFiles() => ["UE4SS.dll", "dwmapi.dll", "UE4SS-settings.ini", Path.Combine("Mods", "mods.txt"), Path.Combine("Mods", "Keybinds", "Scripts", "main.lua"), Path.Combine("Mods", "shared", "UEHelpers", "UEHelpers.lua")];
}
