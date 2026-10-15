using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public sealed record ProfileSwitchResult(bool Success, string? Title = null, string? Message = null, IReadOnlyList<string>? Errors = null);
