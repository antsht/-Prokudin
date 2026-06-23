using Prokudin.Core.Processing;

namespace Prokudin.Core.Diagnostics;

public sealed class NullProcessingDiagnostics : IProcessingDiagnostics
{
    public static NullProcessingDiagnostics Instance { get; } = new();

    private NullProcessingDiagnostics()
    {
    }

    public ProcessingDiagnosticsOptions Options { get; } = new();

    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) => DiagnosticsDisposable.Empty;

    public void Log(ProcessingLogCategory category, string message)
    {
    }

    public void LogComputeAttempt(
        string operation,
        AccelerationBackendKind backend,
        bool succeeded,
        long? elapsedMs = null,
        string? failureReason = null)
    {
    }
}
