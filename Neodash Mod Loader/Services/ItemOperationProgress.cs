using Neodash_Mod_Loader.Models;

namespace Neodash_Mod_Loader.Services;

/// <summary>Labels each item while preserving transfer details and the aggregate percentage.</summary>
public sealed class ItemOperationProgress(IProgress<OperationProgress> progress, string name, int index, int total) : IProgress<OperationProgress>
{
    public void Report(OperationProgress value) => progress.Report(value with
    {
        Message = $"{index + 1} of {total}: {name}\n{value.Message}",
        Percent = value.Percent is double percent ? (index + Math.Clamp(percent, 0, 100) / 100) * 100 / total : null
    });
}
