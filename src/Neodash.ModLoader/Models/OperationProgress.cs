namespace Neodash_Mod_Loader.Models;
public record OperationProgress(string Message, double? Percent = null, long? BytesReceived = null, long? TotalBytes = null, double? BytesPerSecond = null, TimeSpan? EstimatedRemaining = null)
{
    public OperationProgress() : this(string.Empty)
    {
    }
}
