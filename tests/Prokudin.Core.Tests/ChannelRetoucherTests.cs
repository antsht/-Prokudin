using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests;

public sealed class ChannelRetoucherTests
{
    [Fact]
    public void InpaintMask_ReplacesMaskedBrightSpotWithNeighborhood()
    {
        var image = ImageBuffer.Filled(9, 9, 0.5f);
        image[4, 4] = 1.0f;
        var mask = new byte[image.Width * image.Height];
        mask[(4 * image.Width) + 4] = 1;

        var cleaned = ChannelRetoucher.InpaintMask(image, mask, radius: 3);

        cleaned[4, 4].Should().BeLessThan(0.75f);
        cleaned[0, 0].Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void CreateBrushMask_ClampsStrokePointsToImageBounds()
    {
        var stroke = new RetouchStroke(
            [
                new RetouchPoint(-5, -5),
                new RetouchPoint(2, 2),
                new RetouchPoint(10, 10),
            ],
            BrushSize: 3);

        var mask = ChannelRetoucher.CreateBrushMask(5, 5, [stroke]);

        mask.Should().HaveCount(25);
        mask.Should().Contain(value => value > 0);
    }

    [Fact]
    public void AutoClean_ReducesBrightAndDarkSmallDefects()
    {
        var image = ImageBuffer.Filled(21, 21, 0.5f);
        image[6, 6] = 1.0f;
        image[14, 14] = 0.0f;

        var result = ChannelRetoucher.AutoClean(
            image,
            new AutoCleanSettings(Sensitivity: 50, InpaintRadius: 3));

        result.Mask.Should().Contain(value => value > 0);
        result.Image[6, 6].Should().BeLessThan(0.85f);
        result.Image[14, 14].Should().BeGreaterThan(0.15f);
        result.Image[0, 0].Should().BeApproximately(0.5f, 0.01f);
    }
}
