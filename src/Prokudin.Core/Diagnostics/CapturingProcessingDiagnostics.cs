using Prokudin.Core.Processing;

namespace Prokudin.Core.Diagnostics;

public sealed class CapturingProcessingDiagnostics : IProcessingDiagnostics
{
    public List<string> Lines { get; } = [];

    public ProcessingDiagnosticsOptions Options { get; set; } = new(ProcessingLogCategory.All, IncludeTimings: true);

    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
        new ScopedProcessingDiagnostics(this, operationName, category);

    public void Log(ProcessingLogCategory category, string message) => Lines.Add(message);

    public void LogComputeAttempt(
        string operation,
        AccelerationBackendKind backend,
        bool succeeded,
        long? elapsedMs = null,
        string? failureReason = null) =>
        Lines.Add($"{operation}:{backend}:{(succeeded ? "ok" : "fail")}");
}
