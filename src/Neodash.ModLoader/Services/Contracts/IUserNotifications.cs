using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;
public interface IUserNotifications
{
    event EventHandler<UserNotification>? Published;
    void Show(string title, string message);
}
