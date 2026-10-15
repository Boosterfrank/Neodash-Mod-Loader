using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public sealed record InstalledModState(string Name, bool IsEnabled, string EntryPointPath, bool CanRepair, bool CanRestore, string ManagedVersion);
