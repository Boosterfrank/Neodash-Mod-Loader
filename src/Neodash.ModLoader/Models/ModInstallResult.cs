namespace Neodash_Mod_Loader.Models;

/// <summary>Describes the outcome of an install or restore operation.</summary>
public sealed class ModInstallResult
{
    public bool Success { get; set; }

    public List<string> Folders { get; set; } = [];

    public string? BackupPath { get; set; }

    /// <summary>Folders replaced or removed while applying this package, including obsolete package folders.</summary>
    public List<string> ReplacedFolders { get; set; } = [];

    public List<string> Errors { get; set; } = [];
}
