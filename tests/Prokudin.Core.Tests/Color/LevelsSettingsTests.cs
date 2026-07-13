using FluentAssertions;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Color;

public sealed class LevelsSettingsTests
{
    [Fact]
    public void DefaultSettings_MatchesGentleLevels_OnGradient()
    {
        var rgb = TestRgbGradient(64, 64);
        var gentle = ColorCorrection.ApplyGentleLevels(rgb);
        var viaSettings = ColorCorrection.ApplyLevelsSettings(rgb, new LevelsSettings());
        gentle.Pixels.Length.Should().Be(viaSettings.Pixels.Length);
        for (var i = 0; i < gentle.Pixels.Length; i++)
        {
            gentle.Pixels[i].Should().BeApproximately(viaSettings.Pixels[i], 1e-5f);
        }
    }

    [Fact]
    public void ManualLevelsAndGamma_DarkensMidtones()
    {
        var rgb = SolidRgb(0.5f);
        var output = ColorCorrection.ApplyManualLevelsAndGamma(rgb, black: 0.0f, white: 1.0f, gamma: 2.0f);
        output.Pixels[0].Should().BeLessThan(0.5f);
    }

    [Fact]
    public void OffMode_ReturnsOriginalPixels()
    {
        var rgb = SolidRgb(0.42f);
        var output = ColorCorrection.ApplyLevelsSettings(rgb, new LevelsSettings(Mode: LevelsMode.Off));
        output.Should().BeSameAs(rgb);
    }

    [Fact]
    public void AutoMasterLevels_DerivesOneLuminanceCurve_ForEveryRgbChannel()
    {
        var rgb = new RgbImageBuffer(
            3,
            1,
            [
                0.1f, 0.2f, 0.3f,
                0.4f, 0.5f, 0.6f,
                0.7f, 0.8f, 0.9f,
            ]);

        var output = ColorCorrection.ApplyLevelsSettings(
            rgb,
            new LevelsSettings(AutoLowPercent: 0, AutoHighPercent: 100, AutoMaxGain: 2));

        output[1, 0, 0].Should().BeApproximately(0.357f, 0.002f);
        output[1, 0, 1].Should().BeApproximately(0.523f, 0.002f);
        output[1, 0, 2].Should().BeApproximately(0.690f, 0.002f);
    }

    [Fact]
    public void ApplyChannelLevels_OnlyChangesTheConfiguredChannel()
    {
        var rgb = new RgbImageBuffer(1, 1, [0.25f, 0.5f, 0.75f]);

        var output = ColorCorrection.ApplyChannelLevels(
            rgb,
            new ChannelLevelsSettings(
                Red: new ChannelLevelSettings(BlackPoint: 0, WhitePoint: 0.5f, Gamma: 1)));

        output[0, 0, 0].Should().BeApproximately(0.5f, 0.001f);
        output[0, 0, 1].Should().BeApproximately(0.5f, 0.001f);
        output[0, 0, 2].Should().BeApproximately(0.75f, 0.001f);
    }

    private static RgbImageBuffer TestRgbGradient(int width, int height)
    {
        var pixels = new float[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = x / (float)(width - 1);
                var i = ((y * width) + x) * 3;
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
            }
        }

        return new RgbImageBuffer(width, height, pixels);
    }

    private static RgbImageBuffer SolidRgb(float value)
    {
        var pixels = new float[3];
        pixels[0] = value;
        pixels[1] = value;
        pixels[2] = value;
        return new RgbImageBuffer(1, 1, pixels);
    }
}
