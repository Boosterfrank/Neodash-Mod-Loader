using System.Globalization;
using System.Text;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Neodash_Mod_Loader;

/// <summary>Shared Serilog policy for application operations and crash reports.</summary>
internal sealed class ApplicationLogger : IDisposable
{
    internal const string OutputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
    internal const long DefaultFileSizeLimitBytes = 10 * 1024 * 1024;
    internal const int DefaultRetainedFileCount = 14;
    private readonly Lock _sync = new();
    private readonly Func<Logger> _createLogger;
    private Logger? _logger;
    private bool _disposed;

    internal ApplicationLogger(string directory, long fileSizeLimitBytes = DefaultFileSizeLimitBytes, int retainedFiles = DefaultRetainedFileCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileSizeLimitBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedFiles);
        _createLogger = () => CreateLogger(directory, fileSizeLimitBytes, retainedFiles);
    }

    internal ApplicationLogger(Func<Logger> createLogger)
    {
        ArgumentNullException.ThrowIfNull(createLogger);
        _createLogger = createLogger;
    }

    private static Logger CreateLogger(string directory, long fileSizeLimitBytes, int retainedFiles) =>
        new LoggerConfiguration()
            .WriteTo.File(Path.Combine(directory, "operations-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: fileSizeLimitBytes, rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedFiles, shared: true, buffered: false,
                outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture, encoding: new UTF8Encoding(false))
            .WriteTo.Logger(crashes => crashes.Filter.ByIncludingOnly(entry => entry.Level == LogEventLevel.Fatal)
                .WriteTo.File(Path.Combine(directory, "crash.txt"),
                    fileSizeLimitBytes: fileSizeLimitBytes, rollOnFileSizeLimit: true, retainedFileCountLimit: retainedFiles,
                    shared: true, buffered: false, outputTemplate: OutputTemplate,
                    formatProvider: CultureInfo.InvariantCulture, encoding: new UTF8Encoding(false)))
            .CreateLogger();

    internal void Write(string message, Exception? exception = null) =>
        Write(exception is null ? LogEventLevel.Information : LogEventLevel.Error, message, exception);

    internal void WriteCrash(Exception exception) => Write(LogEventLevel.Fatal, "Unhandled exception.", exception);

    private void Write(LogEventLevel level, string message, Exception? exception)
    {
        lock (_sync)
        {
            if (_disposed) return;
            // Only publish successful construction: a transient startup failure can be retried on the next entry.
            var logger = _logger ??= _createLogger();
            logger.Write(level, exception, "{Operation}", message);
        }
    }

    internal static string GetLatestPath(string directory, bool crash = false)
    {
        string fallback = Path.Combine(directory, crash ? "crash.txt" : "operations.log");
        try
        {
            FileInfo? latest = null;
            foreach (string path in Directory.EnumerateFiles(directory, crash ? "crash*.txt" : "operations*.log"))
            {
                var candidate = new FileInfo(path);
                if (latest is null || candidate.LastWriteTimeUtc > latest.LastWriteTimeUtc
                    || (candidate.LastWriteTimeUtc == latest.LastWriteTimeUtc && string.CompareOrdinal(candidate.Name, latest.Name) > 0))
                    latest = candidate;
            }
            return latest?.FullName ?? fallback;
        }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _logger?.Dispose();
        }
    }
}
