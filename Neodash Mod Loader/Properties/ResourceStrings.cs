using System.Resources;

namespace Neodash_Mod_Loader.Properties
{
    internal static class ResourceStrings
    {
        public static string GetRequired(string name)
        {
            return Resources.ResourceManager.GetString(name)
                ?? throw new MissingManifestResourceException($"Required application resource '{name}' was not found.");
        }
    }
}
