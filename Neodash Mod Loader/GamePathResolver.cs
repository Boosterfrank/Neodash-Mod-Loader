using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;

namespace Neodash_Mod_Loader
{
    public static class GamePathResolver
    {
        private static string _cachedPath = null;
        private const string NeodashRelativePath = @"steamapps\common\Neodash\Runway\Binaries\Win64";

        public static string GetNeodashWin64Path()
        {
            if (_cachedPath != null && Directory.Exists(_cachedPath))
            {
                return _cachedPath;
            }

            // 1. Try to find Steam installation path from Registry
            string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            
            if (string.IsNullOrEmpty(steamPath))
            {
                // Fallback to default C drive steam path
                steamPath = @"C:\Program Files (x86)\Steam";
            }

            steamPath = steamPath.Replace('/', '\\');

            // 2. Check default steamapps folder
            string defaultGamePath = Path.Combine(steamPath, NeodashRelativePath);
            if (Directory.Exists(defaultGamePath))
            {
                _cachedPath = defaultGamePath;
                return defaultGamePath;
            }

            // 3. Parse libraryfolders.vdf for other drives
            string vdfPath = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                string content = File.ReadAllText(vdfPath);
                // Simple regex to extract all "path" values
                var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        string libPath = match.Groups[1].Value.Replace("\\\\", "\\");
                        string gamePath = Path.Combine(libPath, NeodashRelativePath);
                        if (Directory.Exists(gamePath))
                        {
                            _cachedPath = gamePath;
                            return gamePath;
                        }
                    }
                }
            }

            // If all else fails, return the default path (even if it doesn't exist yet, so we can create it)
            return defaultGamePath;
        }
    }
}
