namespace Neodash_Mod_Loader.Services;

/// <summary>Window-scoped, non-modal feedback shared by desktop workflows.</summary>
public sealed class UserNotifications(IUiDispatcher dispatcher) : IUserNotifications
{
    public event EventHandler<UserNotification>? Published;

    public void Show(string title, string message) =>
        dispatcher.Post(() => Published?.Invoke(this, new(title, message)));
}
