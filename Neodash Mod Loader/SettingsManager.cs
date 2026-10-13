using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Neodash_Mod_Loader
{
    public static class SettingsManager
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Neodash Mod Loader",
            "settings.json");

        public static Dictionary<string, object> Values { get; private set; }

        static SettingsManager()
        {
            LoadSettings();
        }

        public static void LoadSettings()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    Values = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                    
                    // JSON deserializes numbers differently, need to convert back to correct types if needed.
                    // For bools and strings, it usually wraps them in JsonElement, so let's normalize it.
                    var normalized = new Dictionary<string, object>();
                    foreach (var kvp in Values)
                    {
                        if (kvp.Value is JsonElement element)
                        {
                            switch (element.ValueKind)
                            {
                                case JsonValueKind.True:
                                    normalized[kvp.Key] = true;
                                    break;
                                case JsonValueKind.False:
                                    normalized[kvp.Key] = false;
                                    break;
                                case JsonValueKind.String:
                                    normalized[kvp.Key] = element.GetString();
                                    break;
                                case JsonValueKind.Number:
                                    normalized[kvp.Key] = element.GetDouble();
                                    break;
                                default:
                                    normalized[kvp.Key] = element.ToString();
                                    break;
                            }
                        }
                        else
                        {
                            normalized[kvp.Key] = kvp.Value;
                        }
                    }
                    Values = normalized;
                }
                catch
                {
                    Values = new Dictionary<string, object>();
                }
            }
            else
            {
                Values = new Dictionary<string, object>();
            }
        }

        public static void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                string json = JsonSerializer.Serialize(Values, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
