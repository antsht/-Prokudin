using Prokudin.Core.Diagnostics;

namespace Prokudin.Gui.Services;

public sealed record ProcessingDiagnosticsSettings(
    bool LogComputeBackends = false,
    bool LogPipelineStages = false,
    bool LogCpuParallel = false,
    bool LogTimings = false)
{
    public static ProcessingDiagnosticsSettings Default { get; } = new();

    public ProcessingDiagnosticsOptions ToOptions()
    {
        var categories = ProcessingLogCategory.None;
        if (LogComputeBackends)
        {
            categories |= ProcessingLogCategory.ComputeBackend;
        }

        if (LogPipelineStages)
        {
            categories |= ProcessingLogCategory.PipelineStage;
        }

        if (LogCpuParallel)
        {
            categories |= ProcessingLogCategory.CpuParallel;
        }

        return new ProcessingDiagnosticsOptions(categories, LogTimings);
    }
}
