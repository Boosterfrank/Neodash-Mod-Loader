using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public sealed record ConfigurationLocation(string GameRoot, string FolderRoot, IReadOnlyList<string> Files);
