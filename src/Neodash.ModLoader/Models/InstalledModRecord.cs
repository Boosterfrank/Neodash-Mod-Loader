namespace Neodash_Mod_Loader.Models;
public class InstalledModRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string GamePath { get; set; } = string.Empty;
    public List<string> FolderNames { get; set; } = [];
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; set; }
    public string? BackupPath { get; set; }
    public string? BackupVersion { get; set; }
}
