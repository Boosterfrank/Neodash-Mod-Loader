namespace Neodash_Mod_Loader.Services;

/// <summary>Coalesces background reports into one pending UI callback and drops late reports.</summary>
public sealed class DispatchedProgress<T>(IUiDispatcher dispatcher, Action<T> render) : IProgress<T>, IDisposable
{
    private readonly Lock _gate = new();
    private T? _latest;
    private bool _queued;
    private bool _disposed;

    public void Report(T value)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _latest = value;
            if (_queued) return;
            _queued = true;
        }
        dispatcher.Post(Drain);
    }

    private void Drain()
    {
        lock (_gate)
        {
            _queued = false;
            if (_disposed) return;
            render(_latest!);
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }
}
