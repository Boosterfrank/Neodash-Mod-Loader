using static Neodash.ModLoader.Integration.TestFixtures;
using Neodash_Mod_Loader;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static async Task NativeAndLuaModEntryPointsAreDisplayedCore(string scratch)
    {
        var paths = NewGame(scratch, "entry-points");
        string native = ModManager.GetModPath(paths.Game, "RunwayMP", true);
        Directory.CreateDirectory(Path.Combine(native, "dlls"));
        await File.WriteAllTextAsync(Path.Combine(native, "dlls", "aaa-dependency.dll"), "not an entry point");
        await File.WriteAllTextAsync(Path.Combine(native, "dlls", "main.dll"), "native mod bytes, never executed");
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, native) == @"\Mods\RunwayMP\dlls\main.dll", "native mods display their main DLL instead of a blank Lua path or a dependency");

        string disabled = ModManager.GetModPath(paths.Game, "RunwayMP", false);
        Directory.Move(native, disabled);
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, disabled) == @"\DisabledMods\RunwayMP\dlls\main.dll", "disabled native mods display their current directory");
        Directory.CreateDirectory(Path.Combine(disabled, "Scripts"));
        await File.WriteAllTextAsync(Path.Combine(disabled, "Scripts", "aaa-helper.lua"), "helper");
        await File.WriteAllTextAsync(Path.Combine(disabled, "Scripts", "main.lua"), "entry");
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, disabled) == @"\DisabledMods\RunwayMP\dlls\main.dll; \DisabledMods\RunwayMP\Scripts\main.lua", "mixed native and Lua mods display both main entry points");

        string lua = ModManager.GetModPath(paths.Game, "LegacyLua", true);
        Directory.CreateDirectory(Path.Combine(lua, "Script"));
        await File.WriteAllTextAsync(Path.Combine(lua, "Script", "z-last.lua"), "last");
        await File.WriteAllTextAsync(Path.Combine(lua, "Script", "a-first.LUA"), "first");
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, lua) == @"\Mods\LegacyLua\Script\a-first.LUA", "legacy Lua folders use a deterministic case-insensitive fallback");
        string fallback = ModManager.GetModPath(paths.Game, "NativeFallback", true);
        Directory.CreateDirectory(Path.Combine(fallback, "dlls"));
        await File.WriteAllTextAsync(Path.Combine(fallback, "dlls", "z-last.dll"), "last");
        await File.WriteAllTextAsync(Path.Combine(fallback, "dlls", "a-first.DLL"), "first");
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, fallback) == @"\Mods\NativeFallback\dlls\a-first.DLL", "native fallback displays an actual DLL without trying to load it");
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, ModManager.GetModPath(paths.Game, "Empty", true)) == string.Empty, "missing entry folders have no invented path");
        Xunit.Assert.Throws<IOException>(() => ModEntryPoints.GetDisplayPath(paths.Game, paths.Root));

        string linked = Path.Combine(paths.Game, "Mods", "Linked");
        Directory.CreateDirectory(linked);
        string junction = Path.Combine(linked, "dlls");
        CreateJunction(junction, Path.Combine(disabled, "dlls"));
        try { Xunit.Assert.Throws<IOException>(() => ModEntryPoints.GetDisplayPath(paths.Game, linked)); }
        finally { Directory.Delete(junction); }
    }

    private static async Task NativeModUpdateRemovesObsoleteDllsAndRestoresBackupCore(string scratch)
    {
        var paths = NewGame(scratch, "native-update");
        string live = ModManager.GetModPath(paths.Game, "RunwayMP", false);
        Directory.CreateDirectory(Path.Combine(live, "dlls"));
        await File.WriteAllTextAsync(Path.Combine(live, "dlls", "main.dll"), "old-native");
        await File.WriteAllTextAsync(Path.Combine(live, "dlls", "obsolete.dll"), "old-dependency");
        await File.WriteAllTextAsync(Path.Combine(live, "client.json"), "{\"keep\":true}");
        string package = CreateFolderPackage(paths.Root, "RunwayMP", "dlls/main.dll");

        var update = ModPackageInstaller.InstallFolder(paths.Game, package, paths.Backups, ["RunwayMP"]);
        Assert(update.Success && update.BackupPath is not null, "native DLL update retains a recovery snapshot");
        Assert(await File.ReadAllTextAsync(Path.Combine(live, "dlls", "main.dll")) == "package" && !File.Exists(Path.Combine(live, "dlls", "obsolete.dll")), "new DLL replaces the old DLL and stale dependencies disappear from the live folder");
        Assert(await File.ReadAllTextAsync(Path.Combine(live, "client.json")) == "{\"keep\":true}" && !Directory.Exists(ModManager.GetModPath(paths.Game, "RunwayMP", true)), "native update preserves user configuration and disabled state");
        Assert(ModEntryPoints.GetDisplayPath(paths.Game, live) == @"\DisabledMods\RunwayMP\dlls\main.dll", "updated native mod still exposes its current DLL path");

        var restore = ModPackageInstaller.RestoreBackup(paths.Game, update.BackupPath!, paths.Backups);
        Assert(restore.Success && await File.ReadAllTextAsync(Path.Combine(live, "dlls", "main.dll")) == "old-native" && await File.ReadAllTextAsync(Path.Combine(live, "dlls", "obsolete.dll")) == "old-dependency", "restore recovers both prior native DLLs from the snapshot");
    }
}
