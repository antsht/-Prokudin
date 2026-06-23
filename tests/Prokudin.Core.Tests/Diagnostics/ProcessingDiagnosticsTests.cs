using FluentAssertions;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Diagnostics;

public sealed class ProcessingDiagnosticsTests
{
    [Fact]
    public void NullDiagnostics_DoesNotThrow()
    {
        var diagnostics = NullProcessingDiagnostics.Instance;
        using var scope = diagnostics.BeginScope("test", ProcessingLogCategory.PipelineStage);
        diagnostics.Log(ProcessingLogCategory.ComputeBackend, "ignored");
        diagnostics.LogComputeAttempt("ApplyGain", AccelerationBackendKind.Cpu, succeeded: true);
    }

    [Fact]
    public void FilteringDiagnostics_RespectsCategoryFlags()
    {
        var capture = new CapturingProcessingDiagnostics();
        var diagnostics = new FilteringProcessingDiagnostics(
            capture,
            new ProcessingDiagnosticsOptions(ProcessingLogCategory.ComputeBackend, IncludeTimings: false));

        diagnostics.Log(ProcessingLogCategory.PipelineStage, "hidden");
        diagnostics.Log(ProcessingLogCategory.ComputeBackend, "visible");

        capture.Lines.Should().ContainSingle().Which.Should().Be("visible");
    }

    [Fact]
    public void ScopedDiagnostics_EmitsParallelSummaryOnDispose()
    {
        var capture = new CapturingProcessingDiagnostics();
        var options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.CpuParallel, IncludeTimings: false);
        var root = new FilteringProcessingDiagnostics(capture, options);

        using (root.BeginScope("BuildRgb.merge", ProcessingLogCategory.CpuParallel))
        {
            ProcessingDiagnosticsAmbient.RecordParallel("ForRows", iterationCount: 8192, usedParallel: true, maxDegree: 16);
        }

        capture.Lines.Should().ContainSingle(line => line.Contains("BuildRgb.merge") && line.Contains("ForRows"));
    }
}
