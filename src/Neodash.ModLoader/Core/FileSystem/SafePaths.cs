namespace Neodash_Mod_Loader;

internal static class SafePaths
{
    public static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new IOException("Invalid relative file path.");
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The file path leaves its containing folder.");
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files and folders cannot be changed: " + current);
        }
        return full;
    }

    public static void DeleteScratch(string path)
    {
        string root = Path.GetFullPath(AppStorage.DownloadsPath);
        string checkedPath = Child(root, Path.GetRelativePath(root, path));
        if (!Directory.Exists(checkedPath)) return;
        Stack<string> directories = new();
        directories.Push(checkedPath);
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                Child(root, Path.GetRelativePath(root, entry));
                if (Directory.Exists(entry)) directories.Push(entry);
            }
        }
        Directory.Delete(checkedPath, recursive: true);
    }
}
