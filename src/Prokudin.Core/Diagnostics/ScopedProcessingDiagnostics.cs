namespace Prokudin.Core.Diagnostics;

public sealed class ScopedProcessingDiagnostics : IDisposable
{
    private readonly IProcessingDiagnostics diagnostics;
    private readonly string operationName;
    private string? parallelSummary;

    public ScopedProcessingDiagnostics(
        IProcessingDiagnostics diagnostics,
        string operationName,
        ProcessingLogCategory category)
    {
        this.diagnostics = diagnostics;
        this.operationName = operationName;
        _ = category;
        ProcessingDiagnosticsAmbient.Push(this);
    }

    internal void RecordParallel(string method, long iterationCount, bool usedParallel, int maxDegree)
    {
        parallelSummary = usedParallel
            ? $"{method} {iterationCount} iter, MDP={maxDegree}"
            : $"{method} {iterationCount} iter, sequential";
    }

    public void Dispose()
    {
        ProcessingDiagnosticsAmbient.Pop(this);
        if (parallelSummary is not null)
        {
            diagnostics.Log(
                ProcessingLogCategory.CpuParallel,
                $"[parallel] {operationName}: {parallelSummary}");
        }
    }
}
