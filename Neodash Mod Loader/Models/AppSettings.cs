using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neodash_Mod_Loader.Models
{
    public class AppSettings
    {
        public string? AppTheme { get; set; } = "Dark";
        public bool IsSetupComplete { get; set; }
        public bool ModsTxtMigrated { get; set; }
        public string? GameDirectory { get; set; }
        public List<InstalledModRecord> Installations { get; set; } = [];
        public List<Ue4SsInstallationRecord> Ue4ssInstallations { get; set; } = [];

        [AllowNull]
        public Dictionary<string, string> InstalledMods
        {
            get;
            set => field = value ?? [];
        } = [];

        // Retains unknown settings and reads installed-mod versions saved by older loader versions.
        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalSettings { get; set; } = [];

        public string? GetInstalledModVersion(string modName)
        {
            if (InstalledMods.TryGetValue(modName, out string? version))
            {
                return version;
            }

            return AdditionalSettings.TryGetValue($"InstalledMod_{modName}", out var legacyVersion)
                && legacyVersion.ValueKind == JsonValueKind.String ? legacyVersion.GetString() : null;
        }

        public void SetInstalledModVersion(string modName, string version)
        {
            InstalledMods[modName] = version;
            AdditionalSettings.Remove($"InstalledMod_{modName}");
        }

        public void RemoveInstalledModVersion(string modName)
        {
            InstalledMods.Remove(modName);
            AdditionalSettings.Remove($"InstalledMod_{modName}");
        }
    }
}
