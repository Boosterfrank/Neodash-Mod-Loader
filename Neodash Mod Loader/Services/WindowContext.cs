using Microsoft.UI;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Neodash_Mod_Loader.Services;

public sealed partial class WindowContext : IWindowContext, IDisposable
{
    private readonly Lock _lifetimeLock = new();
    private Window? _window;
    private FrameworkElement? _root;
    private bool _closed;
    public WindowId WindowId => (_window ?? throw new InvalidOperationException("The window has not been attached.")).AppWindow.Id;
    public XamlRoot XamlRoot => _root?.XamlRoot ?? throw new InvalidOperationException("The window's XAML root is not loaded.");

    public void Attach(Window window, FrameworkElement root)
    {
        if (_window is not null && !ReferenceEquals(_window, window))
            throw new InvalidOperationException("A window context belongs to one window.");
        _window = window;
        _root = root;
    }

    public void ApplyTheme(string? theme)
    {
        if (_root is null)
            throw new InvalidOperationException("The window has not been attached.");
        _root.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Default" => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
    }

    public void OpenInstaller(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The downloaded installer does not exist.", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Application.Current.Exit();
    }

    public void Post(Action action)
    {
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher;
        lock (_lifetimeLock)
        {
            if (_closed) return;
            dispatcher = (_window ?? throw new InvalidOperationException("The window has not been attached.")).DispatcherQueue;
        }
        dispatcher.TryEnqueue(() =>
        {
            lock (_lifetimeLock)
            {
                if (!_closed) action();
            }
        });
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            _closed = true;
            _root = null;
            _window = null;
        }
    }
}
