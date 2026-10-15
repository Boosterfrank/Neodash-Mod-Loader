using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IConfigurationService
{
    ConfigurationLocation GetModConfiguration(string name);
    string[] FindFiles(string gameRoot, string folderRoot);
}
