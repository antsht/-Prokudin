using Prokudin.Core.Processing;

namespace Prokudin.Core.Diagnostics;

public sealed class FilteringProcessingDiagnostics : IProcessingDiagnostics
{
    private readonly IProcessingDiagnostics inner;
    private readonly ProcessingDiagnosticsOptions options;

    public FilteringProcessingDiagnostics(IProcessingDiagnostics inner, ProcessingDiagnosticsOptions options)
    {
        this.inner = inner;
        this.options = options;
    }

    public ProcessingDiagnosticsOptions Options => options;

    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
        options.IsEnabled(category)
            ? new ScopedProcessingDiagnostics(this, operationName, category)
            : DiagnosticsDisposable.Empty;

    public void Log(ProcessingLogCategory category, string message)
    {
        if (options.IsEnabled(category))
        {
            inner.Log(category, message);
        }
    }

    public void LogComputeAttempt(
        string operation,
        AccelerationBackendKind backend,
        bool succeeded,
        long? elapsedMs = null,
        string? failureReason = null)
    {
        if (options.IsEnabled(ProcessingLogCategory.ComputeBackend))
        {
            inner.LogComputeAttempt(
                operation,
                backend,
                succeeded,
                options.IncludeTimings ? elapsedMs : null,
                failureReason);
        }
    }
}
