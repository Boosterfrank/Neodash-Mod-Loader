using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

public sealed record OperationProgressPresentation(string Status, string Detail, string Remaining, double? Percent)
{
    public static OperationProgressPresentation From(OperationProgress progress)
    {
        double? percent = progress.Percent is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : null;
        if (progress.BytesReceived is not long received)
            return new(progress.Message, string.Empty, string.Empty, percent);

        string detail = FormatBytes(received);
        if (progress.TotalBytes is > 0)
            detail += " of " + FormatBytes(progress.TotalBytes.Value);
        if (progress.BytesPerSecond is > 0 and var speed && double.IsFinite(speed))
            detail += $" · {FormatBytes(speed)}/s";
        string remaining = GetRemaining(progress, percent);
        return new(progress.Message, detail, remaining, percent);
    }

    private static string GetRemaining(OperationProgress progress, double? percent)
    {
        if (progress.EstimatedRemaining is { } eta && eta > TimeSpan.Zero)
            return "About " + FormatDuration(eta) + " remaining";
        if (percent is >= 100 || progress.TotalBytes is > 0 && progress.BytesReceived >= progress.TotalBytes)
            return "Download complete";
        return progress.TotalBytes is > 0 ? "Estimating time remaining…" : "Time remaining unavailable";
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824:N1} GiB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576:N1} MiB";
        if (bytes >= 1024) return $"{bytes / 1024:N1} KiB";
        return $"{bytes:N0} B";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        double seconds = Math.Ceiling(duration.TotalSeconds);
        if (seconds >= 3600) return $"{Math.Ceiling(seconds / 3600):N0} hr";
        if (seconds >= 60) return $"{Math.Ceiling(seconds / 60):N0} min";
        return $"{seconds:N0} sec";
    }
}
