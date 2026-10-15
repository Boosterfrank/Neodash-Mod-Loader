using System.Globalization;
using Neodash_Mod_Loader;
using Serilog;
using static Neodash.ModLoader.Integration.TestFixtures;

namespace Neodash.ModLoader.Integration;

public sealed partial class IntegrationTests
{
    private static void SerilogFileLogging(string scratch)
    {
        string directory = Path.Combine(scratch, "logs");
        DateTime before = DateTime.Now.AddSeconds(-1);
        using (var logger = new ApplicationLogger(directory))
        {
            logger.Write("Mod operation completed. {literal}");
            var exception = new InvalidOperationException("Outer failure", new IOException("Inner failure"));
            logger.Write("Settings operation failed.", exception);
            Assert(!File.Exists(ApplicationLogger.GetLatestPath(directory, crash: true)), "ordinary operations and handled errors do not create an empty crash report");
            logger.WriteCrash(exception);

            string logPath = ApplicationLogger.GetLatestPath(directory);
            string operations = ReadActiveLog(logPath);
            Assert(operations.Contains("[INF] Mod operation completed. {literal}", StringComparison.Ordinal)
                && operations.Contains("[ERR] Settings operation failed.", StringComparison.Ordinal), "the real Serilog file sink writes readable severity and preserves message braces as data");
            DateTime loggedTime = DateTime.ParseExact(operations.Substring(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Assert(Path.GetFileName(logPath) == "operations-" + loggedTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log", "new operation logs roll daily using the entry's local calendar date");
            Assert(loggedTime >= before && loggedTime <= DateTime.Now, "the real logger uses the running PC's local clock, not UTC or a fixed time zone");
            string crash = ReadActiveLog(ApplicationLogger.GetLatestPath(directory, crash: true));
            Assert(crash.Contains("[FTL] Unhandled exception.", StringComparison.Ordinal)
                && crash.Contains("System.InvalidOperationException: Outer failure", StringComparison.Ordinal)
                && crash.Contains("System.IO.IOException: Inner failure", StringComparison.Ordinal), "crash reports retain exception type, message, and inner exception");
            Assert(!crash.Contains("Mod operation completed", StringComparison.Ordinal)
                && operations.Contains("[FTL] Unhandled exception.", StringComparison.Ordinal), "crash events are available in both diagnostics logs while routine events stay out of the crash report");
        }
    }

    private static void SerilogRotationAndLegacyLogs(string scratch)
    {
        string directory = Path.Combine(scratch, "rolling-logs");
        Directory.CreateDirectory(directory);
        string legacy = Path.Combine(directory, "operations.log");
        File.WriteAllText(legacy, "Legacy log stays intact.");
        Assert(ApplicationLogger.GetLatestPath(directory) == legacy, "existing operation logs are discoverable before the first new-format entry");
        string unrelated = Path.Combine(directory, "settings.json");
        File.WriteAllText(unrelated, "User data stays intact.");
        using (var logger = new ApplicationLogger(directory, fileSizeLimitBytes: 128, retainedFiles: 2))
        {
            for (int index = 0; index < 12; index++) logger.Write("Rotation fixture " + index + new string('x', 128));
            logger.Write("Newest retained event.");
        }
        Assert(Directory.GetFiles(directory, "operations-*.log").Length == 2, "size rolling retains only the configured number of managed log files");
        Assert(File.ReadAllText(ApplicationLogger.GetLatestPath(directory)).Contains("Newest retained event.", StringComparison.Ordinal), "Operation log resolves to the latest rolled file instead of an older base file");
        Assert(File.ReadAllText(legacy) == "Legacy log stays intact." && File.ReadAllText(unrelated) == "User data stays intact.", "rotation preserves old-format logs and unrelated application data");
    }

    private static void SerilogSharedWriters(string scratch)
    {
        string directory = Path.Combine(scratch, "shared-logs");
        using var first = new ApplicationLogger(directory);
        using var second = new ApplicationLogger(directory);
        first.Write("First writer.");
        second.Write("Second writer.");
        string entries = ReadActiveLog(ApplicationLogger.GetLatestPath(directory));
        Assert(entries.Contains("First writer.", StringComparison.Ordinal) && entries.Contains("Second writer.", StringComparison.Ordinal), "shared file sinks append both writers without replacing or losing entries");
    }

    private static void SerilogInitializationRecovery(string scratch)
    {
        Exception[] failures = [new IOException("Temporarily locked."), new UnauthorizedAccessException("Temporarily unavailable.")];
        for (int index = 0; index < failures.Length; index++)
        {
            int attempts = 0;
            string path = Path.Combine(scratch, "retry-" + index + ".log");
            using var logger = new ApplicationLogger(() =>
            {
                if (++attempts == 1) throw failures[index];
                return new LoggerConfiguration().WriteTo.File(path, shared: true).CreateLogger();
            });
            var thrown = Xunit.Assert.ThrowsAny<Exception>(() => logger.Write("First attempt."));
            Assert(ReferenceEquals(thrown, failures[index]), "the logger exposes the original construction failure to its best-effort boundary");
            logger.Write("Recovered entry.");
            logger.Write("Reused logger.");
            Assert(attempts == 2 && ReadActiveLog(path).Contains("Recovered entry.", StringComparison.Ordinal), "a later entry retries failed initialization and then reuses the successful logger");
            logger.Dispose();
            logger.Write("After shutdown.");
            Assert(attempts == 2 && !File.ReadAllText(path).Contains("After shutdown.", StringComparison.Ordinal), "shutdown never recreates the logger or writes through disposed sinks");
        }
    }

    private static string ReadActiveLog(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }
}
