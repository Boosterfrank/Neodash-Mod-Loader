using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public enum AppPage
{
    Home,
    ModLibrary,
    Settings,
    Onboarding,
    Shell
}

public sealed record InstalledModState(string Name, bool IsEnabled, string EntryPointPath, bool CanRepair, bool CanRestore, string ManagedVersion);
public sealed record CatalogModState(bool IsInstalled, bool HasUpdate, bool CanRepair, bool ShowRepair, bool CanInstall, string? InstalledVersion, string Status, string ButtonText)
{
    public bool HasMenuActions => IsInstalled;
}
public sealed record ModFolderLocation(string Name, string Path);
public sealed record ConfigurationLocation(string GameRoot, string FolderRoot, IReadOnlyList<string> Files);
public sealed record ProfileSwitchPlan(ModProfile Target, string GamePath, string PreviousProfile, IReadOnlyList<string> PreviousEnabled, IReadOnlyList<string> MissingMods);
public sealed record ProfileSwitchResult(bool Success, string? Title = null, string? Message = null, IReadOnlyList<string>? Errors = null);
