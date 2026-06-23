using Prokudin.Core.Diagnostics;
using Prokudin.Core.Processing;

namespace Prokudin.Gui.Diagnostics;

public sealed class GuiProcessingDiagnostics : IProcessingDiagnostics
{
    private readonly FilteringProcessingDiagnostics inner;

    public GuiProcessingDiagnostics(Action<string> appendLog, ProcessingDiagnosticsOptions options)
    {
        inner = new FilteringProcessingDiagnostics(new ForwardingDiagnostics(appendLog), options);
    }

    public ProcessingDiagnosticsOptions Options => inner.Options;

    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
        inner.BeginScope(operationName, category);

    public void Log(ProcessingLogCategory category, string message) => inner.Log(category, message);

    public void LogComputeAttempt(
        string operation,
        AccelerationBackendKind backend,
        bool succeeded,
        long? elapsedMs = null,
        string? failureReason = null) =>
        inner.LogComputeAttempt(operation, backend, succeeded, elapsedMs, failureReason);

    private sealed class ForwardingDiagnostics(Action<string> appendLog) : IProcessingDiagnostics
    {
        public ProcessingDiagnosticsOptions Options { get; } = new(ProcessingLogCategory.All, IncludeTimings: true);

        public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
            new ScopedProcessingDiagnostics(this, operationName, category);

        public void Log(ProcessingLogCategory category, string message) => appendLog(message);

        public void LogComputeAttempt(
            string operation,
            AccelerationBackendKind backend,
            bool succeeded,
            long? elapsedMs = null,
            string? failureReason = null)
        {
            var timing = elapsedMs is null ? string.Empty : $" [{elapsedMs}ms]";
            var status = succeeded ? "ok" : $"fail ({failureReason ?? "unknown"})";
            appendLog($"[compute] {operation}: {backend} {status}{timing}");
        }
    }
}
