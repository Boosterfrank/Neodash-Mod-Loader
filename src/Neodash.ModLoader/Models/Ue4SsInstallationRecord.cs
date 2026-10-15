namespace Neodash_Mod_Loader.Models;
public class Ue4SsInstallationRecord
{
    public string GamePath { get; set; } = string.Empty;
    public List<string> Files { get; set; } = [];
    public string? Version { get; set; }
}
