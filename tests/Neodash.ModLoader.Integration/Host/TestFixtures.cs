using System.IO.Compression;
using Neodash_Mod_Loader;

namespace Neodash.ModLoader.Integration;

internal static class TestFixtures
{

    internal static int SnapshotCount(string backupRoot) => !Directory.Exists(backupRoot)
        ? 0
        : Directory.EnumerateDirectories(backupRoot, "snapshot-*", SearchOption.TopDirectoryOnly).Count();

    internal static (string Root, string Game, string Backups) NewGame(string scratch, string name)
    {
        string root = Path.Combine(scratch, name); string game = Path.Combine(root, "Win64");
        Directory.CreateDirectory(Path.Combine(game, "Mods")); Directory.CreateDirectory(Path.Combine(game, "DisabledMods"));
        return (root, game, Path.Combine(root, "backups"));
    }

    internal static string CreateLiveMod(string game, string name, bool enabled)
    {
        string folder = ModManager.GetModPath(game, name, enabled); Directory.CreateDirectory(Path.Combine(folder, "Scripts"));
        string file = Path.Combine(folder, "Scripts", "main.lua"); File.WriteAllText(file, "return true"); return file;
    }

    internal static string CreateFolderPackage(string root, string name, string relativeFile)
    {
        string folder = Path.Combine(root, "packages", name); string target = Path.Combine(folder, relativeFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "package"); return folder;
    }

    internal static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries) { using StreamWriter writer = new(archive.CreateEntry(name).Open()); writer.Write(content); }
    }

    internal static string CreateScratch()
    {
        string root = Path.Combine(FindRepositoryRoot(), "tests", ".scratch"); Directory.CreateDirectory(root);
        return Path.Combine(root, "mod-loader-" + Guid.NewGuid().ToString("N"));
    }

    internal static void DeleteScratch(string scratch)
    {
        string root = Path.GetFullPath(Path.Combine(FindRepositoryRoot(), "tests", ".scratch"));
        string path = Path.GetFullPath(scratch);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing to delete outside tests scratch.");
        if (!Directory.Exists(path)) return;
        Stack<string> pending = new(); pending.Push(path);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Scratch contains a reparse point.");
            foreach (string child in Directory.EnumerateFileSystemEntries(current))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Scratch contains a reparse point.");
                if (Directory.Exists(child))
                    pending.Push(child);
            }
        }
        Directory.Delete(path, true);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Neodash.ModLoader.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    internal static void CreateJunction(string link, string target)
    {
        Environment.SetEnvironmentVariable("NEODASH_TEST_JUNCTION_LINK", link);
        Environment.SetEnvironmentVariable("NEODASH_TEST_JUNCTION_TARGET", target);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "New-Item -ItemType Junction -Path $env:NEODASH_TEST_JUNCTION_LINK -Target $env:NEODASH_TEST_JUNCTION_TARGET | Out-Null" }
        }) ?? throw new InvalidOperationException("PowerShell could not be started to create the junction fixture.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create required junction fixture: " + process.StandardError.ReadToEnd());
    }

    internal static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Assertion failed: " + message);
    }

    internal static TException AssertThrowsAndReturn<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException("Assertion failed: " + message);
    }

    internal static async Task AssertThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Assertion failed: " + message);
    }

    internal static async Task<TException> AssertThrowsAsyncAndReturn<TException>(Func<Task> action, string message) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException("Assertion failed: " + message);
    }

    internal static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Assertion failed: " + message); }
}
