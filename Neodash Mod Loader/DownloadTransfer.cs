using System.Diagnostics;
using System.Security.Cryptography;
using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader;

internal static class DownloadTransfer
{
    internal const long MaximumBytes = 1_073_741_824;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

    internal static async Task CopyAndVerifyAsync(Stream input, Stream output, long? expectedLength,
        string? sha256, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        if (expectedLength > MaximumBytes)
            throw new InvalidDataException("The download is larger than the supported 1 GB limit.");

        token.ThrowIfCancellationRequested();
        progress?.Report(CreateProgress(0, expectedLength, TimeSpan.Zero));
        byte[] buffer = new byte[81920];
        long received = 0;
        var elapsed = Stopwatch.StartNew();
        TimeSpan lastReport = TimeSpan.Zero;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            received += count;
            if (received > MaximumBytes)
                throw new InvalidDataException("The download exceeded the supported 1 GB limit.");

            await output.WriteAsync(buffer.AsMemory(0, count), token);
            hash.AppendData(buffer.AsSpan(0, count));
            if (elapsed.Elapsed - lastReport >= ProgressInterval)
            {
                lastReport = elapsed.Elapsed;
                progress?.Report(CreateProgress(received, expectedLength, lastReport));
            }
        }

        if (expectedLength is not null && received != expectedLength)
            throw new InvalidDataException("The download ended before the advertised content length was received.");

        OperationProgress finalProgress = CreateProgress(received, expectedLength, elapsed.Elapsed);
        progress?.Report(finalProgress);
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            progress?.Report(finalProgress with { Message = "Verifying download…" });
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download checksum did not match the mod library. No game files were changed.");
        }
        progress?.Report(finalProgress with { Message = "Download complete", Percent = 100 });
    }

    internal static OperationProgress CreateProgress(long bytesReceived, long? totalBytes, TimeSpan elapsed)
    {
        double? percent = totalBytes is > 0 ? bytesReceived * 100.0 / totalBytes.Value : null;
        double? bytesPerSecond = elapsed >= ProgressInterval ? bytesReceived / elapsed.TotalSeconds : null;
        TimeSpan? estimatedRemaining = totalBytes is long knownTotal && knownTotal > bytesReceived && bytesPerSecond is > 0
            ? TimeSpan.FromSeconds((knownTotal - bytesReceived) / bytesPerSecond.Value)
            : null;
        return new OperationProgress("Downloading…", percent, bytesReceived, totalBytes, bytesPerSecond, estimatedRemaining);
    }
}
