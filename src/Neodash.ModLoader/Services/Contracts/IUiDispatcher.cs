using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IUiDispatcher
{
    void Post(Action action);
}
