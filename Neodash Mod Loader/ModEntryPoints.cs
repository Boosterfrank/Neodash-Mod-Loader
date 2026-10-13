namespace Neodash_Mod_Loader;

public static class ModEntryPoints
{
    public static string GetDisplayPath(string gamePath, string modPath)
    {
        modPath = SafePaths.Child(gamePath, Path.GetRelativePath(gamePath, modPath));
        List<string> entries = [];
        foreach (string directory in new[] { "dlls", "Scripts", "Script" })
        {
            string root = SafePaths.Child(modPath, directory);
            if (!Directory.Exists(root)) continue;
            string extension = directory == "dlls" ? ".dll" : ".lua";
            string? entry = FindEntry(root, extension);
            if (entry is not null)
                entries.Add(Path.DirectorySeparatorChar + Path.GetRelativePath(gamePath, entry));
        }
        return string.Join("; ", entries);
    }

    private static string? FindEntry(string directory, string extension)
    {
        string? fallback = null;
        foreach (string entry in Directory.EnumerateFiles(directory))
        {
            if (!Path.GetExtension(entry).Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileNameWithoutExtension(entry).Equals("main", StringComparison.OrdinalIgnoreCase))
                return SafePaths.Child(directory, Path.GetFileName(entry));
            if (fallback is null || StringComparer.OrdinalIgnoreCase.Compare(entry, fallback) < 0) fallback = entry;
        }
        return fallback is null ? null : SafePaths.Child(directory, Path.GetFileName(fallback));
    }
}
