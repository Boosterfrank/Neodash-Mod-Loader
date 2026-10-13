using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services.Core;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static void CatalogAvailability(string scratch)
    {
        var paths = NewGame(scratch, "ui-availability");
        File.WriteAllText(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), "");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.Current.Installations.Clear();
        var workspace = new ModWorkspace(new GameService(), new AppDiagnostics());
        var catalog = new ModCatalog(new GameService(), workspace);
        var mod = new ModLibraryItem { Id = "availability", Name = "Availability", Version = "2", DownloadURL = "https://example.invalid/mod.zip", FolderNames = ["Availability"] };

        var absent = catalog.GetState(mod);
        Assert(!absent.HasMenuActions && absent.CanInstall, "an absent mod can be installed but has no menu actions");

        CreateLiveMod(paths.Game, "Availability", true);
        Assert(catalog.GetState(mod).HasMenuActions, "an enabled folder enables catalog menu actions");
        Directory.Move(ModManager.GetModPath(paths.Game, "Availability", true), ModManager.GetModPath(paths.Game, "Availability", false));
        Assert(catalog.GetState(mod).HasMenuActions, "a disabled folder enables catalog menu actions");

        Directory.Delete(ModManager.GetModPath(paths.Game, "Availability", false), true);
        SettingsManager.Current.Installations.Add(new InstalledModRecord { Id = "availability", Name = "Availability", Version = "1", GamePath = paths.Game, FolderNames = ["Availability"] });
        var missing = catalog.GetState(mod);
        Assert(!missing.HasMenuActions && missing.ButtonText == "Repair", "a missing tracked package hides menus and prioritizes repair");

        CreateLiveMod(paths.Game, "Availability", true);
        SettingsManager.Current.Installations[0].FolderNames.Add("Second");
        var partial = catalog.GetState(mod);
        Assert(partial.HasMenuActions && partial.CanRepair, "a partial tracked package keeps menus and repair available");

        SettingsManager.Current.GameDirectory = Path.Combine(scratch, "missing-game");
        var noGame = catalog.GetState(mod);
        Assert(!noGame.CanInstall && !noGame.HasMenuActions, "an unavailable game disables catalog actions");
    }

    private static void RuntimeAndLogAvailability(string scratch)
    {
        var paths = NewGame(scratch, "runtime-availability");
        File.WriteAllText(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), "");
        var game = new GameService();
        var runtime = new RuntimeService(game);
        var diagnostics = new AppDiagnostics();
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.Current.Ue4ssInstallations.RemoveAll(record => ModOperations.SameGame(record.GamePath, paths.Game));
        Assert(!runtime.CanRemove(paths.Game), "no runtime files means removal is unavailable");
        Assert(!diagnostics.CanOpenLocation("ue4ss"), "a missing UE4SS log cannot be opened");
        Assert(!diagnostics.CanOpenLocation("operations"), "a missing operation log cannot be opened");
        Assert(!diagnostics.CanOpenLocation("crash"), "a missing crash log cannot be opened");
        Assert(!Directory.Exists(AppStorage.BackupsPath), "availability checks do not create backups");

        File.WriteAllText(Path.Combine(paths.Game, "dwmapi.dll"), "");
        Assert(!runtime.CanRemove(paths.Game), "an untracked proxy DLL cannot be removed");
        SettingsManager.Current.Ue4ssInstallations.Add(new Ue4SsInstallationRecord
        {
            GamePath = paths.Game,
            Files = ["dwmapi.dll"]
        });
        Assert(runtime.CanRemove(paths.Game), "a tracked present proxy DLL can be removed");
        File.Delete(Path.Combine(paths.Game, "dwmapi.dll"));
        Assert(!runtime.CanRemove(paths.Game), "removal is unavailable after all tracked files are deleted");
        Directory.CreateDirectory(Path.Combine(paths.Game, "ue4ss"));
        File.WriteAllText(Path.Combine(paths.Game, "ue4ss", "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(paths.Game, "UE4SS.dll"), "");
        File.WriteAllText(Path.Combine(paths.Game, "UE4SS.log"), "flat log");
        File.WriteAllText(Path.Combine(paths.Game, "ue4ss", "UE4SS.log"), "nested log");
        SettingsManager.Current.Ue4ssInstallations[0].Files = [Path.Combine("ue4ss", "UE4SS.dll")];
        Assert(runtime.CanRemove(paths.Game), "a tracked present nested runtime DLL can be removed");
        Assert(diagnostics.GetLocation("ue4ss") == Path.Combine(paths.Game, "ue4ss", "UE4SS.log"),
            "the UE4SS log follows the preferred nested runtime layout");
        Assert(diagnostics.CanOpenLocation("ue4ss"), "a created UE4SS log becomes available");
        File.Delete(Path.Combine(paths.Game, "ue4ss", "UE4SS.log"));
        Assert(!diagnostics.CanOpenLocation("ue4ss"), "a deleted UE4SS log becomes unavailable again");

        File.WriteAllText(AppStorage.LogPath, "operation");
        Assert(diagnostics.CanOpenLocation("operations"), "a created operation log becomes available");
        File.Delete(AppStorage.LogPath);
        Assert(!diagnostics.CanOpenLocation("operations"), "a deleted operation log becomes unavailable again");

        string crash = diagnostics.GetLocation("crash");
        File.WriteAllText(crash, "crash");
        Assert(diagnostics.CanOpenLocation("crash"), "a created crash log becomes available");
        File.Delete(crash);
        Assert(!diagnostics.CanOpenLocation("crash"), "a deleted crash log becomes unavailable again");

        SettingsManager.Current.GameDirectory = null;
        Assert(!runtime.CanRemove(""), "an empty path never probes the current directory");
        Assert(!runtime.CanRemove("   "), "a blank path never probes the current directory");
    }
}
