namespace Neodash_Mod_Loader.Services;

public sealed class RuntimeActions(IRuntimeService runtime, IUserDialogs dialogs, IConfigurationEditor editor)
{
    public string GetStatus(string path) => runtime.GetStatus(path);
    public bool HasConfiguration => runtime.HasConfiguration;

    public async Task InstallAsync() => _ = await dialogs.RunAsync("Install / repair UE4SS", (p, t) => runtime.InstallAsync(p, t));
    public async Task RemoveAsync()
    {
        if (!await dialogs.ConfirmAsync("Remove UE4SS?", "Delete the UE4SS runtime DLLs? Your mod folders and configuration files will be kept.", "Remove UE4SS"))
            return;
        await dialogs.RunAsync("Remove UE4SS", (_, _) => runtime.UninstallAsync(), allowCancellation: false);
    }

    public Task EditAsync() => editor.EditRuntimeAsync();
}
