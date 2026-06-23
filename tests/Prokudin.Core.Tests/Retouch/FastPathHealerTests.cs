using FluentAssertions;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class FastPathHealerTests
{
    [Fact]
    public void HealChannel_LargeMask_UsesFastPathStatusWhenModelIsValid()
    {
        var (target, g1, g2, mask) = CreateLargeCorrelatedScene(64, 64, defectPixels: 900);
        var capture = new CapturingProcessingDiagnostics
        {
            Options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.PipelineStage),
        };

        var result = ChannelHealer.HealChannel(
            target,
            g1,
            g2,
            mask,
            new HealOptions(
                LargeMaskFastPathPixelThreshold: 100,
                AllowSoftFastPath: true,
                Diagnostics: capture));

        result.StatusMessage.Should().Contain("Large auto-clean");
        capture.Lines.Should().Contain(line =>
            line.Contains("fast path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HealChannel_LowConfidence_QualityMode_StillUsesFastPathWithSoftAccept()
    {
        var width = 64;
        var height = 64;
        var target = ImageBuffer.Filled(width, height, 0.4f);
        var g1 = ImageBuffer.Filled(width, height, 0.38f);
        var g2 = ImageBuffer.Filled(width, height, 0.42f);
        var mask = new byte[width * height];
        for (var i = 0; i < 900; i++)
        {
            mask[i] = 1;
        }

        var random = new Random(42);
        for (var i = 900; i < mask.Length; i++)
        {
            target.SetNormalized(i, (float)random.NextDouble());
        }

        var capture = new CapturingProcessingDiagnostics
        {
            Options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.PipelineStage),
        };

        var result = ChannelHealer.HealChannel(
            target,
            g1,
            g2,
            mask,
            new HealOptions(
                QualityMode: AutoCleanQualityMode.Quality,
                LargeMaskFastPathPixelThreshold: 100,
                AllowSoftFastPath: true,
                Diagnostics: capture));

        result.StatusMessage.Should().Contain("Large auto-clean");
        capture.Lines.Should().Contain(line => line.Contains("fast path soft-accept"));
    }

    private static (ImageBuffer Target, ImageBuffer G1, ImageBuffer G2, byte[] Mask) CreateLargeCorrelatedScene(
        int width,
        int height,
        int defectPixels)
    {
        var target = ImageBuffer.Filled(width, height, 0.4f);
        var g1 = ImageBuffer.Filled(width, height, 0.38f);
        var g2 = ImageBuffer.Filled(width, height, 0.42f);
        var mask = new byte[width * height];
        for (var i = 0; i < defectPixels; i++)
        {
            mask[i] = 1;
        }

        return (target, g1, g2, mask);
    }
}
