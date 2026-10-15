namespace Neodash_Mod_Loader.Services;

public sealed class OnboardingController(PreferencesController preferences, GameFolderPicker folders, IGameService game, IRuntimeService runtime, IModCatalog catalog, IModWorkspace workspace, IUserDialogs dialogs)
{
    public bool IsBusy => workspace.IsBusy;
    public string GamePath => game.GetPath();

    public Task<bool> ChooseFolderAsync() => folders.ChooseAsync(ensureGameClosed: false);
    public async Task<string?> RunSetupAsync(bool installRecommended)
    {
        if (!await dialogs.RunAsync("Install UE4SS", (p, t) => runtime.InstallAsync(p, t)))
            return null;
        if (!installRecommended)
            return "UE4SS is installed. You can install recommended mods later.";
        bool installed = await dialogs.RunAsync("Install CustomServer", async (progress, token) =>
        {
            var library = await catalog.LoadAsync(token);
            var mod = library.Mods?.Find(m => m.Name.Equals("Neodash Custom Server", StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("CustomServer was not found in the library. You can install it later from Mod Library.");
            await catalog.InstallAsync(mod, progress, token);
        });
        return installed ? "UE4SS and CustomServer are installed." : "UE4SS is installed. CustomServer was not installed; retry it in Mod Library.";
    }

    public void Complete()
    {
        preferences.CompleteSetup();
    }

    public Task ShowErrorAsync(string message) => dialogs.ShowMessageAsync("Setup could not complete", message);
}
