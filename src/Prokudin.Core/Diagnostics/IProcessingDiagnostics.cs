using Prokudin.Core.Processing;

namespace Prokudin.Core.Diagnostics;

public interface IProcessingDiagnostics
{
    ProcessingDiagnosticsOptions Options { get; }

    IDisposable BeginScope(string operationName, ProcessingLogCategory category);

    void Log(ProcessingLogCategory category, string message);

    void LogComputeAttempt(
        string operation,
        AccelerationBackendKind backend,
        bool succeeded,
        long? elapsedMs = null,
        string? failureReason = null);
}
