using FluentAssertions;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Diagnostics;

public sealed class FallbackBackendDiagnosticsTests
{
    [Fact]
    public void FallbackBackend_LogsEachAttempt_WhenDiagnosticsEnabled()
    {
        var capture = new CapturingProcessingDiagnostics
        {
            Options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.ComputeBackend, IncludeTimings: true),
        };
        var diagnostics = new FilteringProcessingDiagnostics(capture, capture.Options);
        var backend = new FallbackImageComputeBackend([new CpuImageComputeBackend()], diagnostics);

        var pixels = new float[64];
        var mask = new byte[64];
        Array.Fill(pixels, 0.5f);

        backend.TryDetectDefectMask(
                pixels,
                pixels,
                pixels,
                pixels,
                pixels,
                pixels,
                coefficientA: 1.0,
                coefficientB: 0.0,
                coefficientC: 0.0,
                residualThreshold: 0.20f,
                highPassThreshold: 0.10f,
                supportMultiplier: 1.50f,
                supportOffset: 0.01f,
                mask)
            .Should()
            .BeTrue();

        capture.Lines.Should().Contain(line => line.Contains("Cpu", StringComparison.Ordinal) && line.Contains("ok", StringComparison.Ordinal));
    }
}
