namespace Neodash_Mod_Loader.Services;

public sealed class WindowDispatcher(IWindowContext window) : IUiDispatcher
{
    public void Post(Action action) => window.Post(action);
}
