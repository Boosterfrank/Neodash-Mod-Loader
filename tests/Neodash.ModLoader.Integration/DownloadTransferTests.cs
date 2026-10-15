using System.Security.Cryptography;
using Neodash_Mod_Loader;
using Neodash_Mod_Loader.Models;
using Xunit;

namespace Neodash.ModLoader.Integration;

public sealed class DownloadTransferTests
{
    [Fact]
    public void CreateProgressForKnownLengthIncludesPercentSpeedAndEta()
    {
        OperationProgress progress = DownloadTransfer.CreateProgress(1024, 4096, TimeSpan.FromSeconds(2));

        Assert.Equal(25d, progress.Percent);
        Assert.Equal(1024, progress.BytesReceived);
        Assert.Equal(4096, progress.TotalBytes);
        Assert.Equal(512d, progress.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(6), progress.EstimatedRemaining);
    }

    [Fact]
    public void CreateProgressForUnknownLengthRemainsIndeterminate()
    {
        OperationProgress progress = DownloadTransfer.CreateProgress(1024, null, TimeSpan.FromSeconds(2));

        Assert.Null(progress.Percent);
        Assert.Equal(1024, progress.BytesReceived);
        Assert.Null(progress.TotalBytes);
        Assert.Equal(512d, progress.BytesPerSecond);
        Assert.Null(progress.EstimatedRemaining);
    }

    [Fact]
    public async Task CopyAndVerifyKnownLengthWritesBytesAndReportsCompletion()
    {
        byte[] bytes = "known-length-download"u8.ToArray();
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var reports = new List<OperationProgress>();
        string checksum = Convert.ToHexString(SHA256.HashData(bytes));

        await DownloadTransfer.CopyAndVerifyAsync(input, output, bytes.Length, checksum,
            new InlineProgress(reports), CancellationToken.None);

        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(0, reports[0].BytesReceived);
        OperationProgress completed = reports.Last(report => report.Message.StartsWith("Downloading", StringComparison.Ordinal));
        Assert.Equal(bytes.Length, completed.BytesReceived);
        Assert.Equal(bytes.Length, completed.TotalBytes);
        Assert.Equal(100, completed.Percent);
        Assert.Contains(reports, report => report.Message == "Verifying download…");
    }

    [Fact]
    public async Task UnknownLengthTransferKeepsActualByteCountUntilVerifiedCompletion()
    {
        using var input = new MemoryStream([1, 2, 3]);
        using var output = new MemoryStream();
        List<OperationProgress> reports = [];
        await DownloadTransfer.CopyAndVerifyAsync(input, output, null, null, new InlineProgress(reports), CancellationToken.None);
        Assert.All(reports.Where(report => report.Message.StartsWith("Downloading", StringComparison.Ordinal)), report => Assert.Null(report.Percent));
        Assert.Equal(3, reports[^1].BytesReceived);
        Assert.Null(reports[^1].TotalBytes);
        Assert.Equal(100, reports[^1].Percent);
        Assert.Equal("Download complete", reports[^1].Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Fact]
    public async Task OversizedAdvertisedDownloadIsRejectedBeforeAnyOutput()
    {
        using var input = new MemoryStream([1]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => DownloadTransfer.CopyAndVerifyAsync(input, output, DownloadTransfer.MaximumBytes + 1, null, null, CancellationToken.None));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task CopyAndVerifyRejectsTruncatedKnownLength()
    {
        using var input = new MemoryStream([1, 2, 3]);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => DownloadTransfer.CopyAndVerifyAsync(input, output, 4, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task CopyAndVerifyRejectsChecksumMismatch()
    {
        using var input = new MemoryStream([1, 2, 3]);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => DownloadTransfer.CopyAndVerifyAsync(input, output, 3, new string('0', 64), null, CancellationToken.None));
    }

    [Fact]
    public async Task CopyAndVerifyHonorsCancellationDuringRead()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingStream(cancellation);
        using var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadTransfer.CopyAndVerifyAsync(input, output, null, null, null, cancellation.Token));
        Assert.Empty(output.ToArray());
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(cancellation.Token);
        }
    }

    private sealed class InlineProgress(List<OperationProgress> reports) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => reports.Add(value);
    }
}
