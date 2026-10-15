using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Neodash_Mod_Loader.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Neodash_Mod_Loader.Ui;

/// <summary>Keeps the native window off screen until its page, final size, and presentation are ready.</summary>
public sealed partial class StartupWindowReveal(IAppDiagnostics diagnostics) : IDisposable
{
    private const uint DwmWindowAttributeCloak = 13;
    private readonly DispatcherTimer _fallback = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly StartupFrameGate _frames = new();
    private readonly Stopwatch _elapsed = new();
    private Window? _window;
    private FrameworkElement? _root;
    private Func<bool>? _contentReady;
    private Action? _onReady;
    // Borrowed from WinUI; the Window owns this HWND and destroys it on close.
    private nint _handle;
    private bool _cloaked;
    private bool _waiting;
    private bool _presentationPending;
    private bool _disposed;

    public void Prepare(Window window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null) throw new InvalidOperationException("Startup presentation is already attached.");
        _window = window;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _elapsed.Start();
        // Apply before InitializeComponent too: XAML/backdrop initialization can create native surfaces.
        _cloaked = SetCloaked(true);
        window.Closed += WindowClosed;
        diagnostics.Log($"Startup window prepared (DWM cloaked: {_cloaked}; executable: {Environment.ProcessPath}).");
    }

    public void WaitForContent(FrameworkElement root, Func<bool> contentReady, Action onReady)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is null || _waiting) throw new InvalidOperationException("Prepare the startup window before attaching its content once.");
        _root = root;
        _contentReady = contentReady;
        _onReady = onReady;
        _waiting = true;
        CompositionTarget.Rendered += FrameRendered;
        _fallback.Tick += FallbackTick;
        _fallback.Start();
    }

    private StartupFrameSnapshot GetFrame()
    {
        if (_root is not { IsLoaded: true, XamlRoot: { } xamlRoot } || _window is null)
            return default;
        var client = _window.AppWindow.ClientSize;
        return new(_contentReady?.Invoke() == true, _root.ActualWidth, _root.ActualHeight,
            xamlRoot.RasterizationScale, client.Width, client.Height);
    }

    private void FrameRendered(object? sender, RenderedEventArgs args)
    {
        if (!_waiting || _disposed || _presentationPending) return;
        if (!_frames.Observe(GetFrame()))
        {
            // Explicitly schedule the confirming layout/render, including otherwise-static pages.
            if (_frames.HasCandidate) _root?.InvalidateArrange();
            return;
        }
        _presentationPending = true;
        _ = WaitForPresentationAsync();
    }

    private async Task WaitForPresentationAsync()
    {
        var dispatcher = _window!.DispatcherQueue;
        try
        {
            // Rendered is a XAML-side event, not a desktop Present fence. Do not block the UI thread.
            int result = await Task.Run(DwmFlush).ConfigureAwait(false);
            dispatcher.TryEnqueue(() => PresentationCompleted(result));
        }
        catch (Exception ex)
        {
            dispatcher.TryEnqueue(() => PresentationCompleted(Marshal.GetHRForException(ex), ex));
        }
    }

    private void PresentationCompleted(int result, Exception? failure = null)
    {
        if (!_waiting || _disposed) return;
        _presentationPending = false;
        if (result < 0)
            diagnostics.Log("Startup presentation fence was unavailable.", failure ?? Marshal.GetExceptionForHR(result));
        // A resize or a late page load while DWM was presenting needs another matching frame.
        if (!_frames.Observe(GetFrame()))
        {
            if (_frames.HasCandidate) _root?.InvalidateArrange();
            return;
        }
        Reveal(result < 0 ? "page rendered; presentation fence unavailable" : "page and window size rendered; presentation completed");
    }

    private void FallbackTick(object? sender, object args) => Reveal("startup render/presentation fallback");

    private void Reveal(string reason)
    {
        if (!_waiting || _disposed) return;
        if (_cloaked && !SetCloaked(false)) return;
        _cloaked = false;
        _waiting = false;
        diagnostics.Log($"Startup window revealed after {_elapsed.ElapsedMilliseconds} ms ({reason}); {GetFrame()}.");
        StopObserving();
        Action? ready = _onReady;
        _onReady = null;
        ready?.Invoke();
    }

    private bool SetCloaked(bool cloaked)
    {
        int value = cloaked ? 1 : 0; // Win32 BOOL is four bytes.
        int result = DwmSetWindowAttribute(_handle, DwmWindowAttributeCloak, in value, sizeof(int));
        if (result >= 0) return true;
        diagnostics.Log("Could not change startup window visibility.", Marshal.GetExceptionForHR(result));
        return false;
    }

    private void StopObserving()
    {
        _fallback.Stop();
        _fallback.Tick -= FallbackTick;
        CompositionTarget.Rendered -= FrameRendered;
        _window?.Closed -= WindowClosed;
        _root = null;
        _contentReady = null;
    }

    private void WindowClosed(object sender, WindowEventArgs args)
    {
        _cloaked = false; // The HWND is being destroyed; never use a possibly-reused handle.
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopObserving();
        if (_cloaked) _ = SetCloaked(false);
        _cloaked = false;
        _waiting = false;
        _window = null;
        _onReady = null;
    }

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint window, uint attribute, in int value, uint size);

    // No marshalling is needed; keep this import available during design-time compilation too.
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmFlush();
}
