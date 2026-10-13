using Neodash_Mod_Loader.Models;
using Neodash_Mod_Loader.Services;

namespace Neodash_Mod_Loader.Services.Core;

/// <summary>Coordinates profile selection with the established filesystem and profile persistence safeguards.</summary>
public sealed class ProfileService(IGameService game) : IProfileService
{
    public IReadOnlyList<ModProfile> Profiles => ProfileManager.Profiles;
    public ModProfile ActiveProfile => ProfileManager.ActiveProfile;
    public int MaxNameLength => ProfileManager.MaxNameLength;

    public string? ValidateName(string? name, ModProfile? renaming = null) => ProfileManager.ValidateName(name, renaming);
    public void Create(string name) => ProfileManager.CreateProfile(name, game.RequirePath());
    public void Rename(ModProfile profile, string name) => ProfileManager.RenameProfile(profile, name);
    public void Delete(ModProfile profile) => ProfileManager.DeleteProfile(profile);
    public void Export(string path) => ProfileManager.ExportProfile(path);
    public ModProfile Import(string path) => ProfileManager.ImportProfile(path);
    public ProfileSwitchPlan PrepareSwitch(ModProfile target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ProfileManager.EnsureWritable();
        string path = game.RequirePath();
        ProfileManager.EnsureProfileGamePath(target, path);
        game.EnsureClosed(path);
        var installedMods = ModManager.GetMods(path);
        HashSet<string> installed = new(installedMods.Select(mod => mod.Name), StringComparer.OrdinalIgnoreCase);
        List<string> missing = target.EnabledMods.Where(name => !installed.Contains(name)).ToList();
        List<string> previousEnabled = installedMods.Where(mod => mod.IsEnabled).Select(mod => mod.Name).ToList();
        return new ProfileSwitchPlan(target, path, ActiveProfile.Name, previousEnabled, missing);
    }

    public ProfileSwitchResult CommitSwitch(ProfileSwitchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        game.EnsureClosed(plan.GamePath);
        List<string> errors = ModManager.ApplyEnabledMods(plan.GamePath, plan.Target.EnabledMods);
        if (errors.Count > 0)
        {
            return new ProfileSwitchResult(false, "Couldn't switch profiles", $"Your mods were left as they were in \"{plan.PreviousProfile}\".", errors);
        }

        try
        {
            ProfileManager.SetActiveProfile(plan.Target, plan.GamePath);
            return new ProfileSwitchResult(true);
        }
        catch (Exception ex)
        {
            List<string> rollbackErrors = ModManager.ApplyEnabledMods(plan.GamePath, plan.PreviousEnabled.ToList());
            string detail = rollbackErrors.Count == 0 ? "The original mod set was restored." : "The profile was not saved, and some folders could not be restored:\n" + string.Join("\n", rollbackErrors);
            return new ProfileSwitchResult(false, "Profile switch needs attention", "The selected profile could not be saved: " + ex.Message + "\n\n" + detail);
        }
    }
}
