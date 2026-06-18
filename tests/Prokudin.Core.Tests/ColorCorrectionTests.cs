using FluentAssertions;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests;

public sealed class ColorCorrectionTests
{
    [Fact]
    public void ApplyPipetteBalance_NeutralizesSamplePatch()
    {
        var rgb = SolidRgb(100, 100, 0.5f, 0.6f, 0.4f);

        var balanced = ColorCorrection.ApplyPipetteBalance(rgb, 50, 50, radius: 5);

        var means = PatchMeans(balanced, 48, 48, 53, 53);
        means[1].Should().BeApproximately(means[0], 0.03f);
        means[2].Should().BeApproximately(means[0], 0.03f);
    }

    [Fact]
    public void ApplyTempTint_WarmsImage()
    {
        var rgb = SolidRgb(20, 20, 0.5f, 0.5f, 0.5f);

        var warm = ColorCorrection.ApplyTempTint(rgb, temperature: 50, tint: 0);

        warm[0, 0, 0].Should().BeGreaterThan(rgb[0, 0, 0]);
        warm[0, 0, 2].Should().BeLessThan(rgb[0, 0, 2]);
    }

    private static RgbImageBuffer SolidRgb(int width, int height, float red, float green, float blue)
    {
        var pixels = new float[width * height * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = red;
            pixels[i + 1] = green;
            pixels[i + 2] = blue;
        }

        return new RgbImageBuffer(width, height, pixels);
    }

    private static float[] PatchMeans(RgbImageBuffer rgb, int x0, int y0, int x1, int y1)
    {
        var sums = new float[3];
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    sums[c] += rgb[x, y, c];
                }

                count++;
            }
        }

        return sums.Select(sum => sum / count).ToArray();
    }
}
