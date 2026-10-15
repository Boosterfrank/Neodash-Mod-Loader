using System.Text.Json;
using System.Text.Json.Nodes;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader
{
    public static class SettingsManager
    {
        private static readonly string SettingsDirectory = AppStorage.DirectoryPath;
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
        private static bool _canSaveSettings = true;
        public static string? LoadError { get; private set; }
        public static AppSettings Current { get; private set; } = new();

        static SettingsManager()
        {
            LoadSettings();
        }

        public static void LoadSettings()
        {
            _canSaveSettings = true;
            LoadError = null;
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    var loaded = DeserializeSettings(json);
                    loaded.Installations ??= [];
                    loaded.Ue4ssInstallations ??= [];
                    loaded.AdditionalSettings ??= [];
                    ValidateSettings(loaded);
                    Current = loaded;
                }
                catch (Exception ex)
                {
                    Current = new();
                    // A corrupt or incompatible file must stay intact until it can be loaded successfully.
                    _canSaveSettings = false;
                    LoadError = $"Settings could not be loaded. Your existing file has been preserved: {ex.Message}";
                    AppStorage.Log(LoadError, ex);
                }
            }
            else
            {
                Current = new();
            }
        }

        public static void SaveSettings()
        {
            EnsureWritable();

            try
            {
                ValidateSettings(Current);
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings);
                AppStorage.WriteAtomically(SettingsPath, json);
            }
            catch (Exception ex)
            {
                AppStorage.Log("Could not save settings.", ex);
                throw;
            }
        }

        public static void EnsureWritable()
        {
            if (!_canSaveSettings) throw new InvalidOperationException(LoadError);
        }

        private static AppSettings DeserializeSettings(string json)
        {
            var document = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("Settings must contain a JSON object.");

            int schemaVersion = 1; // Unversioned files use the original settings schema.
            if (document.TryGetPropertyValue(nameof(AppSettings.SchemaVersion), out var schema)
                && (schema is not JsonValue versionValue || !versionValue.TryGetValue<int>(out schemaVersion)))
                throw new JsonException("Settings SchemaVersion must be an integer.");
            ValidateSchemaVersion(schemaVersion);

            // Version 1.3 stores top-level arrays/objects as JSON strings when it resaves newer settings.
            // Decode only known collection fields; unknown settings and ordinary strings stay intact.
            foreach (string name in new[] { "Installations", "Ue4ssInstallations", "InstalledMods" })
            {
                if (document[name] is JsonValue value && value.TryGetValue<string>(out string? encoded))
                    document[name] = JsonNode.Parse(encoded);
            }

            return JsonSerializer.Deserialize(document.ToJsonString(), AppJsonContext.Default.AppSettings)
                ?? throw new JsonException("Settings must contain a JSON object.");
        }

        private static void ValidateSettings(AppSettings settings)
        {
            ValidateSchemaVersion(settings.SchemaVersion);
            if (!string.IsNullOrWhiteSpace(settings.GameDirectory)) ValidateGamePath(settings.GameDirectory);
            ValidateModRecords(settings.Installations);
            ValidateRuntimeRecords(settings.Ue4ssInstallations);
        }

        private static void ValidateSchemaVersion(int version)
        {
            if (version > AppSettings.CurrentSchemaVersion)
                throw new JsonException($"Settings format {version} requires a newer version of Neodash Mod Loader. Your settings will not be overwritten.");
            if (version < 1)
                throw new JsonException("Settings SchemaVersion must be a positive integer.");
            if (version != AppSettings.CurrentSchemaVersion)
                throw new JsonException($"Settings format {version} needs a migration that this build does not support. Your settings will not be overwritten.");
        }

        private static void ValidateModRecords(IEnumerable<InstalledModRecord> records)
        {
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                if (record is null || string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.Name)
                    || string.IsNullOrWhiteSpace(record.Version) || record.FolderNames is not { Count: > 0 })
                    throw new JsonException("An installed-mod record is incomplete.");
                string game = ValidateGamePath(record.GamePath);
                if (!identities.Add(game + "\0" + record.Id)) throw new JsonException("Installed-mod records contain duplicate identities.");
                foreach (string folder in record.FolderNames)
                {
                    ValidateModFolder(folder);
                    if (!owners.Add(game + "\0" + folder)) throw new JsonException("Multiple installed-mod records claim the same folder.");
                }
                ValidateModSource(record);
            }
        }

        private static void ValidateModSource(InstalledModRecord record)
        {
            if (record.DownloadUrl is not null && (!Uri.TryCreate(record.DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps))
                throw new JsonException("An installed-mod download address must use HTTPS.");
            if (record.Sha256 is not null && (record.Sha256.Length != 64 || record.Sha256.Any(c => !Uri.IsHexDigit(c))))
                throw new JsonException("An installed-mod checksum is invalid.");
            if (record.BackupPath is not null) ValidateGamePath(record.BackupPath);
        }

        private static void ValidateRuntimeRecords(IEnumerable<Ue4SsInstallationRecord> records)
        {
            var runtimeGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var runtime in records)
            {
                if (runtime is null || runtime.Files is not { Count: > 0 }) throw new JsonException("A UE4SS installation record is incomplete.");
                string game = ValidateGamePath(runtime.GamePath);
                if (!runtimeGames.Add(game)) throw new JsonException("Duplicate UE4SS installation records were found.");
                foreach (string file in runtime.Files) ValidateRuntimeFile(file);
            }
        }

        private static void ValidateRuntimeFile(string? file)
        {
            if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) throw new JsonException("A UE4SS installation contains an invalid relative file path.");
            foreach (string segment in file.Split(['/', '\\'], StringSplitOptions.None))
                if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new JsonException("A UE4SS installation contains an invalid relative file path.");
        }

        private static string ValidateGamePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new JsonException("Installation and backup paths must be valid absolute paths.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private static void ValidateModFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder != folder.Trim() || folder.EndsWith('.') || folder is "." or ".."
                || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder.Equals("Keybinds", StringComparison.OrdinalIgnoreCase)
                || folder.Equals("shared", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("An installed-mod record contains an invalid folder name.");
            string stem = folder.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new JsonException("An installed-mod record contains a reserved Windows folder name.");
        }

        public static AppSettings CaptureSnapshot() => JsonSerializer.Deserialize(
            JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings), AppJsonContext.Default.AppSettings)
            ?? throw new JsonException("Could not snapshot settings.");

        public static void RestoreSnapshot(AppSettings snapshot, bool persist = true)
        {
            Current = snapshot;
            if (persist) SaveSettings();
        }
    }
}
