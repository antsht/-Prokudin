using FluentAssertions;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Processing;

public sealed class ImageComputeBackendTests
{
    [Fact]
    public void CpuBackend_DetectDefectMask_MatchesExpectedMask()
    {
        var target = new[] { 0.10f, 0.90f, 0.30f, 0.50f };
        var guide1 = new[] { 0.10f, 0.20f, 0.30f, 0.50f };
        var guide2 = new[] { 0.10f, 0.20f, 0.30f, 0.50f };
        var targetHighPass = new[] { 0.01f, 0.30f, 0.01f, 0.04f };
        var guide1HighPass = new[] { 0.01f, 0.02f, 0.01f, 0.04f };
        var guide2HighPass = new[] { 0.01f, 0.02f, 0.01f, 0.04f };
        var mask = new byte[target.Length];

        var backend = ImageComputeBackendFactory.CreateCpu();
        var used = backend.TryDetectDefectMask(
            target,
            guide1,
            guide2,
            targetHighPass,
            guide1HighPass,
            guide2HighPass,
            coefficientA: 1.0,
            coefficientB: 0.0,
            coefficientC: 0.0,
            residualThreshold: 0.20f,
            highPassThreshold: 0.10f,
            supportMultiplier: 1.50f,
            supportOffset: 0.01f,
            mask);

        used.Should().BeTrue();
        mask.Should().Equal(0, 1, 0, 0);
    }

    [Fact]
    public void CpuBackend_PredictMasked_LeavesUnmaskedPixelsAndPredictsMaskedPixels()
    {
        var target = new[] { 0.10f, 0.20f, 0.30f, 0.40f };
        var guide1 = new[] { 0.20f, 0.30f, 0.40f, 0.50f };
        var guide2 = new[] { 0.10f, 0.10f, 0.10f, 0.10f };
        var mask = new byte[] { 0, 1, 0, 1 };
        var output = new float[target.Length];

        var backend = ImageComputeBackendFactory.CreateCpu();
        var used = backend.TryPredictMasked(
            target,
            guide1,
            guide2,
            mask,
            coefficientA: 0.5,
            coefficientB: 0.25,
            coefficientC: 0.1,
            output);

        used.Should().BeTrue();
        output.Should().Equal(
            0.10f,
            (0.30f * 0.5f) + (0.10f * 0.25f) + 0.1f,
            0.30f,
            (0.50f * 0.5f) + (0.10f * 0.25f) + 0.1f);
    }

    [Fact]
    public void CpuBackend_ApplyGain_ClampsNormalizedPixels()
    {
        var source = new[] { 0.10f, 0.50f, 0.75f };
        var output = new float[source.Length];

        var backend = ImageComputeBackendFactory.CreateCpu();
        var used = backend.TryApplyGain(source, gain: 2.0f, output);

        used.Should().BeTrue();
        output.Should().Equal(0.20f, 1.00f, 1.00f);
    }

    [Fact]
    public void CreateBest_ReturnsCheapWrappers_OverSharedLeafBackends()
    {
        using var first = (FallbackImageComputeBackend)ImageComputeBackendFactory.CreateBest(NullProcessingDiagnostics.Instance);
        using var second = (FallbackImageComputeBackend)ImageComputeBackendFactory.CreateBest(new CapturingProcessingDiagnostics());

        first.Should().NotBeSameAs(second);
        first.Backends.Should().HaveSameCount(second.Backends);
        first.Backends.Zip(second.Backends).Should().OnlyContain(pair => ReferenceEquals(pair.First, pair.Second));
    }

    [Fact]
    public void FallbackBackend_DisposesChildren_OnlyWhenItOwnsThem()
    {
        var sharedChild = new DisposableBackend();
        using (new FallbackImageComputeBackend([sharedChild], ownsBackends: false))
        {
        }

        sharedChild.DisposeCount.Should().Be(0);

        var ownedChild = new DisposableBackend();
        using (new FallbackImageComputeBackend([ownedChild], ownsBackends: true))
        {
        }

        ownedChild.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void IlgpuCpuBackend_WhenAvailable_MatchesCpuPrediction()
    {
        using var ilgpu = ImageComputeBackendFactory.TryCreateIlgpuCpu(out var backend)
            ? backend
            : null;
        if (ilgpu is null)
        {
            return;
        }

        var target = Enumerable.Range(0, 64).Select(i => i / 100.0f).ToArray();
        var guide1 = Enumerable.Range(0, 64).Select(i => (63 - i) / 100.0f).ToArray();
        var guide2 = Enumerable.Range(0, 64).Select(i => (i % 7) / 10.0f).ToArray();
        var mask = Enumerable.Range(0, 64).Select(i => i % 3 == 0 ? (byte)1 : (byte)0).ToArray();
        var expected = new float[target.Length];
        var actual = new float[target.Length];

        ImageComputeBackendFactory.CreateCpu().TryPredictMasked(
            target,
            guide1,
            guide2,
            mask,
            coefficientA: 0.35,
            coefficientB: 0.20,
            coefficientC: 0.05,
            expected).Should().BeTrue();

        ilgpu.TryPredictMasked(
            target,
            guide1,
            guide2,
            mask,
            coefficientA: 0.35,
            coefficientB: 0.20,
            coefficientC: 0.05,
            actual).Should().BeTrue();

        actual.Should().Equal(expected, (left, right) => Math.Abs(left - right) < 1e-6f);
    }

    [Fact]
    public void IlgpuCpuBackend_WhenAvailable_MatchesCpuGain()
    {
        using var ilgpu = ImageComputeBackendFactory.TryCreateIlgpuCpu(out var backend)
            ? backend
            : null;
        if (ilgpu is null)
        {
            return;
        }

        var source = Enumerable.Range(0, 64).Select(i => i / 50.0f).ToArray();
        var expected = new float[source.Length];
        var actual = new float[source.Length];

        ImageComputeBackendFactory.CreateCpu().TryApplyGain(source, gain: 1.75f, expected).Should().BeTrue();
        ilgpu.TryApplyGain(source, gain: 1.75f, actual).Should().BeTrue();

        actual.Should().Equal(expected, (left, right) => Math.Abs(left - right) < 1e-6f);
    }

    private sealed class DisposableBackend : IImageComputeBackend
    {
        public int DisposeCount { get; private set; }

        public AccelerationBackendKind Kind => AccelerationBackendKind.Cpu;

        public bool TryDetectDefectMask(
            float[] target,
            float[] other1,
            float[] other2,
            float[] targetHighPass,
            float[] other1HighPass,
            float[] other2HighPass,
            double coefficientA,
            double coefficientB,
            double coefficientC,
            float residualThreshold,
            float highPassThreshold,
            float supportMultiplier,
            float supportOffset,
            byte[] outputMask) => true;

        public bool TryPredictMasked(
            float[] target,
            float[] guide1,
            float[] guide2,
            byte[] defectMask,
            double coefficientA,
            double coefficientB,
            double coefficientC,
            float[] output) => true;

        public bool TryApplyGain(float[] source, float gain, float[] output) => true;

        public bool TryHighPassAbs(float[] source, int width, int height, double sigma, float[] output) => true;

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
