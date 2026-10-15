using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public sealed record CatalogModState(bool IsInstalled, bool HasUpdate, bool CanRepair, bool ShowRepair, bool CanInstall, string? InstalledVersion, string Status, string ButtonText)
{
    public bool HasMenuActions => IsInstalled;
}
