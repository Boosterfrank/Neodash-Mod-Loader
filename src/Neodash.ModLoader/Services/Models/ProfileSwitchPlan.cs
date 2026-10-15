using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public sealed record ProfileSwitchPlan(ModProfile Target, string GamePath, string PreviousProfile, IReadOnlyList<string> PreviousEnabled, IReadOnlyList<string> MissingMods);
