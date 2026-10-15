using System.Diagnostics;
using System.Text;
using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Services.Core;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static async Task LuaCreationCore(string scratch)
    {
        var paths = NewGame(scratch, "lua-create");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), "");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Lua fixture", paths.Game);

        Assert(ModOperations.ValidateLuaModName("") is not null, "an empty Lua mod name is rejected");
        Assert(ModOperations.ValidateLuaModName(new string('a', 65)) is not null, "a Lua mod name longer than 64 characters is rejected");
        Assert(ModOperations.ValidateLuaModName(" Leading") is not null, "a Lua mod name with leading whitespace is rejected");
        Assert(ModOperations.ValidateLuaModName("Trailing ") is not null, "a Lua mod name with trailing whitespace is rejected");
        Assert(ModOperations.ValidateLuaModName(";comment") is not null, "a Lua mod name that starts with a mods.txt comment marker is rejected");
        Assert(ModOperations.ValidateLuaModName("bad/name") is not null, "a Lua mod name with a path separator is rejected");
        Assert(ModOperations.ValidateLuaModName("CON.foo.lua") is not null, "a Lua mod name with a reserved Windows stem is rejected");
        Assert(ModOperations.ValidateLuaModName(new string('z', ModOperations.MaximumLuaModNameLength)) is null, "a 64-character Lua mod name is accepted");
        string modsTxt = Path.Combine(paths.Game, "Mods", "mods.txt");
        Assert(!File.Exists(modsTxt), "the creation fixture starts without mods.txt");
        await AssertThrowsAsync<ArgumentException>(
            () => ModOperations.CreateLuaModAsync("MODS.TXT"),
            "creation refuses the mods.txt control-file name case-insensitively");
        Assert(!File.Exists(modsTxt), "rejecting the mods.txt control-file name does not create mods.txt");
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "MODS.TXT")), "rejecting the mods.txt control-file name does not create a mod folder");

        await ModOperations.CreateLuaModAsync("LuaCreated");

        string script = Path.Combine(paths.Game, "Mods", "LuaCreated", "Scripts", "main.lua");
        Assert(File.Exists(script), "Lua creation writes main.lua into an enabled mod folder");
        Assert((await File.ReadAllTextAsync(script)).Contains("[LuaCreated] Mod loaded\\n", StringComparison.Ordinal), "Lua creation writes the load message");
        Assert(!(await File.ReadAllBytesAsync(script)).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Lua creation writes main.lua without a UTF-8 byte order mark");
        Assert(ModOperations.FindRecordForFolder("LuaCreated")?.Version == "local", "Lua creation records a local installation");
        Assert(ModOperations.FindRecordForFolder("LuaCreated") is { BackupPath: null }, "a fresh Lua mod does not expose its transaction snapshot as a restore backup");
        Assert(ProfileManager.ActiveProfile.EnabledMods.Contains("LuaCreated"), "Lua creation enables the active profile");
        Assert((await File.ReadAllTextAsync(modsTxt)).Contains("LuaCreated : 1", StringComparison.Ordinal), "Lua creation enables the mod in a newly created mods.txt");
        Assert(ModOperations.ValidateLuaModName("LuaCreated") is not null, "an existing enabled Lua mod is rejected");

        string maximumName = new('z', ModOperations.MaximumLuaModNameLength);
        await ModOperations.CreateLuaModAsync(maximumName);
        Assert(File.Exists(Path.Combine(paths.Game, "Mods", maximumName, "Scripts", "main.lua")), "a 64-character Lua mod name creates a mod");

        Directory.CreateDirectory(Path.Combine(paths.Game, "DisabledMods", "DisabledTaken"));
        Assert(ModOperations.ValidateLuaModName("DisabledTaken") is not null, "an existing disabled Lua mod is rejected");

        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Mods", "FileTaken"), "reserved by another file");
        Assert(ModOperations.ValidateLuaModName("FileTaken") is not null, "a matching file in Mods reserves the Lua mod name");

        SettingsManager.Current.Installations.Add(new InstalledModRecord
        {
            Id = "tracked-only",
            Name = "Tracked only",
            Version = "local",
            GamePath = paths.Game,
            FolderNames = ["TrackedMissing"]
        });
        Assert(ModOperations.ValidateLuaModName("TrackedMissing") is not null, "a tracked package reserves a missing Lua mod folder name");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await AssertThrowsAsync<OperationCanceledException>(
            () => ModOperations.CreateLuaModAsync("Cancelled", token: cancelled.Token),
            "a cancelled Lua creation request does not begin a transaction");
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "Cancelled")), "cancelled Lua creation leaves no mod folder");
        AssertNoLuaStagingFolders("cancelled Lua creation cleans its staging directory");

        GameRuntime.RunningProbe = _ => true;
        await AssertThrowsAsync<InvalidOperationException>(() => ModOperations.CreateLuaModAsync("Blocked"), "a running game blocks Lua creation");
        GameRuntime.RunningProbe = null;
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "Blocked")), "blocked Lua creation leaves no mod folder");
        AssertNoLuaStagingFolders("blocked Lua creation does not create a staging directory");

        byte[] modsTxtBeforeFailure = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("; a preserved comment\r\nLuaCreated : 1\r\n"))
            .ToArray();
        await File.WriteAllBytesAsync(modsTxt, modsTxtBeforeFailure);
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        await AssertThrowsAsync<IOException>(
            () => ModOperations.CreateLuaModAsync("CommitFailure"),
            "a failed installation commit reports the settings write failure");
        AppStorage.FailSettingsWriteNumber = null;
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "CommitFailure")), "a failed installation commit removes the new mod folder");
        Assert(ModOperations.FindRecordForFolder("CommitFailure") is null, "a failed installation commit restores installation metadata");
        Assert((await File.ReadAllBytesAsync(modsTxt)).SequenceEqual(modsTxtBeforeFailure), "a failed installation commit restores the exact previous mods.txt bytes");
        AssertNoLuaStagingFolders("a failed installation commit cleans its staging directory");

        File.Delete(modsTxt);
        AppStorage.FailSettingsWriteNumber = AppStorage.SettingsWriteCount + 2;
        await AssertThrowsAsync<IOException>(
            () => ModOperations.CreateLuaModAsync("MissingModsTxtFailure"),
            "a failed installation commit reports failure after creating mods.txt");
        AppStorage.FailSettingsWriteNumber = null;
        Assert(!File.Exists(modsTxt), "a failed installation commit removes newly created mods.txt");
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "MissingModsTxtFailure")), "a failed installation commit removes the new mod when mods.txt was absent");
        AssertNoLuaStagingFolders("a failed installation commit with no prior mods.txt cleans its staging directory");

        string profilesPath = Path.Combine(AppStorage.DirectoryPath, "profiles.json");
        string settingsPath = Path.Combine(AppStorage.DirectoryPath, "settings.json");
        byte[] profileBytesBeforeFailure = await File.ReadAllBytesAsync(profilesPath);
        byte[] settingsBytesBeforeFailure = await File.ReadAllBytesAsync(settingsPath);
        string[] enabledBeforeFailure = [.. ProfileManager.ActiveProfile.EnabledMods];
        byte[] modsTxtBeforeProfileFailure = Encoding.UTF8.GetBytes("LuaCreated : 1\n");
        await File.WriteAllBytesAsync(modsTxt, modsTxtBeforeProfileFailure);
        AppStorage.FailWritePath = profilesPath;
        await AssertThrowsAsync<IOException>(
            () => ModOperations.CreateLuaModAsync("ProfileWriteFailure"),
            "a failed profile write reports the transactional error");
        AppStorage.FailWritePath = null;
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "ProfileWriteFailure")), "a failed profile write removes the new mod folder");
        Assert(ModOperations.FindRecordForFolder("ProfileWriteFailure") is null, "a failed profile write restores installation metadata");
        Assert(ProfileManager.ActiveProfile.EnabledMods.SequenceEqual(enabledBeforeFailure), "a failed profile write restores active-profile selections");
        Assert((await File.ReadAllBytesAsync(profilesPath)).SequenceEqual(profileBytesBeforeFailure), "a failed profile write preserves profiles.json bytes");
        Assert((await File.ReadAllBytesAsync(settingsPath)).SequenceEqual(settingsBytesBeforeFailure), "a failed profile write preserves settings.json bytes");
        Assert((await File.ReadAllBytesAsync(modsTxt)).SequenceEqual(modsTxtBeforeProfileFailure), "a failed profile write restores mods.txt bytes");
        AssertNoLuaStagingFolders("a failed profile write cleans its staging directory");
    }

    private static async Task LuaCreationRefusalsCore(string scratch)
    {
        var paths = NewGame(scratch, "lua-refusals");
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), "");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();
        ProfileManager.CreateProfile("Lua refusals", paths.Game);

        string modsTxt = Path.Combine(paths.Game, "Mods", "mods.txt");
        byte[] modsTxtBytes = Encoding.UTF8.GetBytes("Existing : 1\n");
        await File.WriteAllBytesAsync(modsTxt, modsTxtBytes);
        string enabledScript = CreateLiveMod(paths.Game, "Existing", true);
        byte[] enabledScriptBytes = await File.ReadAllBytesAsync(enabledScript);
        string disabledScript = CreateLiveMod(paths.Game, "DisabledExisting", false);
        byte[] disabledScriptBytes = await File.ReadAllBytesAsync(disabledScript);
        await File.WriteAllTextAsync(Path.Combine(paths.Game, "Mods", "FileCollision"), "do not replace");
        SettingsManager.Current.Installations.Add(new InstalledModRecord
        {
            Id = "tracked-missing",
            Name = "Tracked missing",
            Version = "local",
            GamePath = paths.Game,
            FolderNames = ["TrackedMissing"]
        });

        await AssertThrowsAsync<ArgumentException>(
            () => ModOperations.CreateLuaModAsync("existing"),
            "creation refuses an enabled mod collision case-insensitively");
        await AssertThrowsAsync<ArgumentException>(
            () => ModOperations.CreateLuaModAsync("disabledexisting"),
            "creation refuses a disabled mod collision case-insensitively");
        await AssertThrowsAsync<ArgumentException>(
            () => ModOperations.CreateLuaModAsync("FileCollision"),
            "creation refuses a file collision");
        await AssertThrowsAsync<ArgumentException>(
            () => ModOperations.CreateLuaModAsync("TrackedMissing"),
            "creation refuses a tracked missing-folder collision");
        await AssertThrowsAsync<ArgumentException>(
            () => ModOperations.CreateLuaModAsync("CON.foo.lua"),
            "creation refuses a reserved Windows name");
        Assert((await File.ReadAllBytesAsync(enabledScript)).SequenceEqual(enabledScriptBytes), "enabled collision refusal preserves existing mod bytes");
        Assert((await File.ReadAllBytesAsync(disabledScript)).SequenceEqual(disabledScriptBytes), "disabled collision refusal preserves existing mod bytes");
        Assert((await File.ReadAllBytesAsync(modsTxt)).SequenceEqual(modsTxtBytes), "collision refusals preserve mods.txt bytes");

        using var cancelled = new CancellationTokenSource();
        var cancellingProgress = new CancellingProgress(cancelled);
        await AssertThrowsAsync<OperationCanceledException>(
            () => ModOperations.CreateLuaModAsync("CancelDuringStaging", cancellingProgress, cancelled.Token),
            "progress-time cancellation aborts Lua creation before installation");
        Assert(!Directory.Exists(Path.Combine(paths.Game, "Mods", "CancelDuringStaging")), "mid-staging cancellation leaves no mod folder");
        Assert(ModOperations.FindRecordForFolder("CancelDuringStaging") is null, "mid-staging cancellation leaves no installation record");
        AssertNoLuaStagingFolders("mid-staging cancellation cleans its staging directory");

        int closedChecks = 0;
        GameRuntime.RunningProbe = _ =>
        {
            closedChecks++;
            if (closedChecks == 2)
            {
                string external = Path.Combine(paths.Game, "Mods", "LateCollision");
                Directory.CreateDirectory(external);
                File.WriteAllText(Path.Combine(external, "external.txt"), "created after snapshot");
            }

            return false;
        };
        await AssertThrowsAsync<IOException>(
            () => ModOperations.CreateLuaModAsync("LateCollision"),
            "creation rechecks for a folder added after its snapshot");
        GameRuntime.RunningProbe = null;
        string externalFile = Path.Combine(paths.Game, "Mods", "LateCollision", "external.txt");
        Assert(File.Exists(externalFile), "late collision handling preserves the external folder");
        Assert((await File.ReadAllTextAsync(externalFile)) == "created after snapshot", "late collision handling preserves external content");
        Assert(ModOperations.FindRecordForFolder("LateCollision") is null, "late collision handling creates no installation record");
        AssertNoLuaStagingFolders("late collision handling cleans its staging directory");
    }

    private static void LuaEditorCore(string scratch)
    {
        var paths = NewGame(scratch, "lua-editor");
        File.WriteAllText(Path.Combine(paths.Game, "Runway-Win64-Shipping.exe"), "");
        SettingsManager.Current.GameDirectory = paths.Game;
        SettingsManager.SaveSettings();

        string enabled = CreateLiveMod(paths.Game, "LuaEnabled", true);
        string disabled = CreateLiveMod(paths.Game, "LuaDisabled", false);
        string enabledRoot = Path.GetDirectoryName(Path.GetDirectoryName(enabled))!;
        Directory.CreateDirectory(Path.Combine(enabledRoot, "dlls"));
        File.WriteAllText(Path.Combine(enabledRoot, "dlls", "mixed.dll"), "");
        string singular = Path.Combine(paths.Game, "Mods", "LuaSingular", "Script");
        Directory.CreateDirectory(singular);
        File.WriteAllText(Path.Combine(singular, "entry.lua"), "return true");
        Directory.CreateDirectory(Path.Combine(paths.Game, "Mods", "Native", "dlls"));
        File.WriteAllText(Path.Combine(paths.Game, "Mods", "Native", "dlls", "main.dll"), "");

        var workspace = new ModWorkspace(new GameService(), new AppDiagnostics());

        Assert(workspace.GetLuaScriptFolder("LuaEnabled") == Path.GetDirectoryName(enabled), "the workspace resolves the enabled Scripts folder");
        Assert(workspace.GetLuaScriptFolder("LuaDisabled") == Path.GetDirectoryName(disabled), "the workspace resolves the disabled Scripts folder");
        Assert(workspace.GetLuaScriptFolder("LuaSingular") == singular, "singular Script folders containing Lua are discovered through the workspace");
        Assert(workspace.GetLuaScriptFolder("Native") is null, "native-only mods have no Lua folder");

        ProcessStartInfo? captured = null;
        var editor = new VisualStudioCode(() => "Code.exe", info => captured = info);
        Assert(editor.IsAvailable, "an editor with a resolved executable is available");
        editor.OpenFolder(Path.Combine(paths.Game, "Mods", "LuaEnabled", "Scripts"));
        Assert(captured is not null && !captured.UseShellExecute && captured.ArgumentList.SequenceEqual(["--new-window", "--", Path.GetFullPath(Path.Combine(paths.Game, "Mods", "LuaEnabled", "Scripts"))]), "VS Code launches without a shell and separates the folder argument");

        var unavailable = new VisualStudioCode(() => null, _ => throw new InvalidOperationException("an unavailable editor must not launch"));
        Assert(!unavailable.IsAvailable, "an editor without a resolved executable is unavailable");
        AssertThrows<FileNotFoundException>(() => unavailable.OpenFolder(Path.Combine(paths.Game, "Mods", "LuaEnabled", "Scripts")), "a missing editor is reported");
        AssertThrows<DirectoryNotFoundException>(() => editor.OpenFolder(Path.Combine(paths.Game, "Mods", "Missing")), "a missing Lua folder is reported before attempting a launch");

        string spacedFolder = Path.Combine(paths.Game, "Mods", "Lua & Space", "Scripts");
        Directory.CreateDirectory(spacedFolder);
        File.WriteAllText(Path.Combine(spacedFolder, "main.lua"), "return true");
        editor.OpenFolder(spacedFolder);
        Assert(captured is not null && captured.ArgumentList.SequenceEqual(["--new-window", "--", Path.GetFullPath(spacedFolder)]), "shell metacharacters and spaces stay in one folder argument");

        var launchFailure = new VisualStudioCode(
            () => "Code.exe",
            _ => throw new InvalidOperationException("Injected editor launch failure."));
        AssertThrows<InvalidOperationException>(() => launchFailure.OpenFolder(spacedFolder), "editor launch failures propagate to the caller");
    }

    private static void AssertNoLuaStagingFolders(string message)
    {
        if (!Directory.Exists(AppStorage.DownloadsPath))
        {
            return;
        }

        Assert(!Directory.EnumerateDirectories(AppStorage.DownloadsPath, "lua-*", SearchOption.TopDirectoryOnly).Any(), message);
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            cancellation.Cancel();
        }
    }
}
