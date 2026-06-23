using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class FastModeHealerTests
{
    [Fact]
    public void HealChannel_FastMode_SkipsGuidedPatchForHighConfidencePixels()
    {
        var (target, g1, g2, mask) = CreateLargeCorrelatedScene(48, 48, defectPixels: 400);
        var result = ChannelHealer.HealChannel(
            target,
            g1,
            g2,
            mask,
            new HealOptions(
                QualityMode: AutoCleanQualityMode.Fast,
                LargeMaskFastPathPixelThreshold: 100,
                UseGuidedPatchSearch: false));

        result.UsedCrossChannel.Should().BeTrue();
        result.StatusMessage.Should().Contain("Fast");
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
