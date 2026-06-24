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
