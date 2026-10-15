namespace Neodash_Mod_Loader.Ui;

public readonly record struct StartupFrameSnapshot(bool ContentReady, double Width, double Height,
    double Scale, int ClientWidth, int ClientHeight);

/// <summary>Rejects incomplete or stale-size frames and requires a subsequent matching frame.</summary>
public sealed class StartupFrameGate
{
    private const double ScaleTolerance = 0.000001;
    private StartupFrameSnapshot? _previous;
    public bool HasCandidate => _previous is not null;

    public bool Observe(StartupFrameSnapshot frame)
    {
        if (!IsComplete(frame))
        {
            _previous = null;
            return false;
        }

        bool stable = _previous is { } previous && previous.ClientWidth == frame.ClientWidth
            && previous.ClientHeight == frame.ClientHeight && Math.Abs(previous.Scale - frame.Scale) < ScaleTolerance;
        _previous = frame;
        return stable;
    }

    private static bool IsComplete(StartupFrameSnapshot frame)
    {
        if (!frame.ContentReady || frame.Width <= 0 || frame.Height <= 0 || frame.Scale <= 0
            || frame.ClientWidth <= 0 || frame.ClientHeight <= 0)
            return false;
        double width = frame.Width * frame.Scale;
        double height = frame.Height * frame.Scale;
        return double.IsFinite(width) && double.IsFinite(height)
            && Math.Abs(width - frame.ClientWidth) <= 1 && Math.Abs(height - frame.ClientHeight) <= 1;
    }
}
