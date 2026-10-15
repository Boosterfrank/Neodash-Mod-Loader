using System.Globalization;
using Neodash_Mod_Loader;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class ApplicationLoggerTests
{
    [Fact]
    public void DefaultLogPolicyKeepsFourteenTenMebibyteFiles()
    {
        Assert.Equal(10 * 1024 * 1024, ApplicationLogger.DefaultFileSizeLimitBytes);
        Assert.Equal(14, ApplicationLogger.DefaultRetainedFileCount);
    }

    [Fact]
    public void SerilogTemplateUsesReadableLocalTimestampAndPreservesLiteralMessages()
    {
        var timestamp = new DateTimeOffset(2026, 10, 15, 4, 27, 1, 16, TimeSpan.FromHours(2));
        var entry = new LogEvent(timestamp, LogEventLevel.Information, null,
            new MessageTemplateParser().Parse("{Operation}"),
            [new LogEventProperty("Operation", new ScalarValue("Mod operation completed. {literal}"))]);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new MessageTemplateTextFormatter(ApplicationLogger.OutputTemplate, CultureInfo.InvariantCulture).Format(entry, output);

        Assert.Equal("[2026-10-15 04:27:01] [INF] Mod operation completed. {literal}" + Environment.NewLine, output.ToString());
    }
}
