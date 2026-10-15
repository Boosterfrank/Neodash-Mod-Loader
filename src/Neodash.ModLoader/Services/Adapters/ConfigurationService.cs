using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Safe configuration discovery that leaves editing to the presentation layer.</summary>
public sealed class ConfigurationService(IGameService game, IModWorkspace workspace) : IConfigurationService
{
    public ConfigurationLocation GetModConfiguration(string name)
    {
        string root = workspace.GetModFolder(name);
        string gameRoot = game.RequirePath();
        return new ConfigurationLocation(gameRoot, root, FindFiles(gameRoot, root));
    }

    public string[] FindFiles(string gameRoot, string folderRoot)
    {
        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, folderRoot)));
        while (directories.Count > 0)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                string safe = SafePaths.Child(gameRoot, Path.GetRelativePath(gameRoot, entry));
                if (Directory.Exists(safe))
                    directories.Push(safe);
                else if (new[]
                {
                    ".ini",
                    ".cfg",
                    ".toml",
                    ".json"
                }.Contains(Path.GetExtension(safe), StringComparer.OrdinalIgnoreCase))
                    files.Add(safe);
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files.ToArray();
    }
}
