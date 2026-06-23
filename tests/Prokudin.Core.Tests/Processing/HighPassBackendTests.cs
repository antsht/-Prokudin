using FluentAssertions;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Processing;

public sealed class HighPassBackendTests
{
    [Fact]
    public void CpuHighPass_MatchesOpenCvReference_WithinTolerance()
    {
        var source = Enumerable.Range(0, 64).Select(i => (float)i / 64f).ToArray();
        var cpu = new CpuImageComputeBackend();
        var output = new float[64];
        cpu.TryHighPassAbs(source, width: 8, height: 8, sigma: 2.0, output).Should().BeTrue();
        output.Should().AllSatisfy(v => v.Should().BeGreaterThanOrEqualTo(0));

        var reference = new float[64];
        HighPassFilter.Compute(source, width: 8, height: 8, sigma: 2.0, reference);
        for (var i = 0; i < output.Length; i++)
        {
            output[i].Should().BeApproximately(reference[i], 0.0001f);
        }
    }
}
