using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Neodash_Mod_Loader;

public static partial class GamePathResolver
{
    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathPattern();

    public static string? NormalizeGameDirectory(string directory)
    {
        foreach (string candidate in new[] { directory, Path.Combine(directory, "Runway", "Binaries", "Win64"), Path.Combine(directory, "Binaries", "Win64") })
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Runway-Win64-Shipping.exe")))
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        }
        return null;
    }

    public static string GetNeodashWin64Path()
    {
        if (!string.IsNullOrWhiteSpace(SettingsManager.Current.GameDirectory))
            return NormalizeGameDirectory(SettingsManager.Current.GameDirectory) ?? string.Empty;

        string steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")).Replace('/', '\\');
        List<string> libraries = [steam];
        try
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match match in LibraryPathPattern().Matches(File.ReadAllText(vdf)))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppStorage.Log("Could not read Steam libraries.", ex);
        }
        foreach (string library in libraries)
        {
            string? found = NormalizeGameDirectory(Path.Combine(library, "steamapps", "common", "Neodash"));
            if (found is not null) return found;
        }
        return string.Empty;
    }

    public static string RequireGamePath() => GetNeodashWin64Path() is { Length: > 0 } path ? path
        : throw new DirectoryNotFoundException("Neodash was not found. Choose its installation folder in Settings or Setup.");
}
