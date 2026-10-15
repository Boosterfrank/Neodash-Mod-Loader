using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IProfileService
{
    IReadOnlyList<ModProfile> Profiles { get; }

    ModProfile ActiveProfile { get; }

    int MaxNameLength { get; }

    string? ValidateName(string? name, ModProfile? renaming = null);
    void Create(string name);
    void Rename(ModProfile profile, string name);
    void Delete(ModProfile profile);
    void Export(string path);
    ModProfile Import(string path);
    ProfileSwitchPlan PrepareSwitch(ModProfile target);
    ProfileSwitchResult CommitSwitch(ProfileSwitchPlan plan);
}
