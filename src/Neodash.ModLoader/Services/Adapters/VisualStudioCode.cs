using System.Diagnostics;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Launches the desktop editor directly, passing the folder as one argument without a shell.</summary>
public sealed class VisualStudioCode : ICodeEditor
{
    private readonly Func<string?> _findExecutable;
    private readonly Action<ProcessStartInfo> _launch;

    public VisualStudioCode() : this(FindExecutable, info => Process.Start(info)?.Dispose()) { }

    internal VisualStudioCode(Func<string?> findExecutable, Action<ProcessStartInfo> launch)
    {
        _findExecutable = findExecutable;
        _launch = launch;
    }

    public bool IsAvailable => _findExecutable() is not null;

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The Lua script folder no longer exists. Refresh the mod list.");
        string executable = _findExecutable() ?? throw new FileNotFoundException("VS Code was not found. Install VS Code, then reopen this menu, or use Open mod folder.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.ArgumentList.Add("--new-window");
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(Path.GetFullPath(path));
        _launch(info);
    }

    private static string? FindExecutable()
    {
        foreach (string candidate in GetCandidates())
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    private static IEnumerable<string> GetCandidates()
    {
        foreach (var (folder, relative) in new[]
        {
            (Environment.SpecialFolder.LocalApplicationData, Path.Combine("Programs", "Microsoft VS Code", "Code.exe")),
            (Environment.SpecialFolder.ProgramFiles, Path.Combine("Microsoft VS Code", "Code.exe")),
            (Environment.SpecialFolder.ProgramFilesX86, Path.Combine("Microsoft VS Code", "Code.exe"))
        })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length > 0) yield return Path.Combine(root, relative);
        }
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            yield return Path.Combine(directory, "Code.exe");
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)).Equals("bin", StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(directory, "..", "Code.exe");
        }
    }
}
