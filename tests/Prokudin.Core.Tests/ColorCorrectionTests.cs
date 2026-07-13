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
    public void ApplyColorSettings_UsesPipetteOnlyWhenActive()
    {
        var rgb = SolidRgb(20, 20, 0.3f, 0.6f, 0.9f);

        var inactive = ColorCorrection.ApplyColorSettings(
            rgb,
            new ColorSettings(
                AutoWhiteBalance: false,
                PipetteActive: false,
                PipetteX: 10,
                PipetteY: 10));
        var active = ColorCorrection.ApplyColorSettings(
            rgb,
            new ColorSettings(
                AutoWhiteBalance: false,
                PipetteActive: true,
                PipetteX: 10,
                PipetteY: 10));

        inactive[0, 0, 0].Should().BeApproximately(0.3f, 0.001f);
        inactive[0, 0, 1].Should().BeApproximately(0.6f, 0.001f);
        inactive[0, 0, 2].Should().BeApproximately(0.9f, 0.001f);
        active[0, 0, 0].Should().BeApproximately(active[0, 0, 1], 0.03f);
        active[0, 0, 1].Should().BeApproximately(active[0, 0, 2], 0.03f);
    }

    [Fact]
    public void ApplyColorSettings_WhitePickWithoutCommittedSample_LeavesBaseBalanceUnchanged()
    {
        var rgb = SolidRgb(20, 20, 0.3f, 0.6f, 0.9f);

        var unchanged = ColorCorrection.ApplyColorSettings(
            rgb,
            new ColorSettings(WhiteBalanceSource.WhitePick));

        unchanged.Should().BeSameAs(rgb);
    }

    [Fact]
    public void ApplyColorSettings_WhitePickWithCommittedSample_NeutralizesSamplePatch()
    {
        var rgb = SolidRgb(20, 20, 0.3f, 0.6f, 0.9f);

        var balanced = ColorCorrection.ApplyColorSettings(
            rgb,
            new ColorSettings(
                WhiteBalanceSource.WhitePick,
                WhitePick: new WhitePick(10, 10, Radius: 3)));

        balanced[0, 0, 0].Should().BeApproximately(balanced[0, 0, 1], 0.03f);
        balanced[0, 0, 1].Should().BeApproximately(balanced[0, 0, 2], 0.03f);
    }

    [Theory]
    [InlineData(0.03f, 0.03f, 0.03f, WhitePickQualityIssue.TooDark)]
    [InlineData(0.8f, 0.2f, 0.2f, WhitePickQualityIssue.StronglyColored)]
    public void EvaluateWhitePick_ReportsUnreliableSampleReason(
        float red,
        float green,
        float blue,
        WhitePickQualityIssue expectedIssue)
    {
        var rgb = SolidRgb(7, 7, red, green, blue);

        var quality = ColorCorrection.EvaluateWhitePick(rgb, new WhitePick(3, 3, Radius: 3));

        quality.Issue.Should().Be(expectedIssue);
        quality.HasWarning.Should().BeTrue();
    }

    [Fact]
    public void EvaluateWhitePick_ReportsHighlyTexturedPatch()
    {
        var pixels = new float[7 * 7 * 3];
        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                var value = (x + y) % 2 == 0 ? 0.2f : 0.8f;
                var i = ((y * 7) + x) * 3;
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
            }
        }

        var quality = ColorCorrection.EvaluateWhitePick(new RgbImageBuffer(7, 7, pixels), new WhitePick(3, 3, Radius: 3));

        quality.Issue.Should().Be(WhitePickQualityIssue.HighlyTextured);
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
