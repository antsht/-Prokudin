namespace Prokudin.Core.Diagnostics;

[Flags]
public enum ProcessingLogCategory
{
    None = 0,
    ComputeBackend = 1,
    PipelineStage = 2,
    CpuParallel = 4,
    All = ComputeBackend | PipelineStage | CpuParallel,
}
