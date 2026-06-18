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

    [Fact]
    public void Stamp_MapsSourceAnchorToDestinationAnchor()
    {
        var image = ImageBuffer.Filled(12, 8, 0.1f);
        image[2, 3] = 0.9f;

        var result = ChannelRetoucher.Stamp(
            image,
            new CloneStampStroke(
                SourceAnchor: new RetouchPoint(2, 3),
                DestinationStroke: new RetouchStroke([new RetouchPoint(8, 4)], BrushSize: 1),
                BlendWidth: 1));

        result.Image[8, 4].Should().BeApproximately(0.9f, 0.001f);
        result.Mask[(4 * image.Width) + 8].Should().Be(1);
        image[8, 4].Should().BeApproximately(0.1f, 0.001f);
    }

    [Fact]
    public void Stamp_FeathersDestinationMaskEdge()
    {
        var image = ImageBuffer.Filled(24, 16, 0.1f);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image[x, y] = 0.9f;
            }
        }

        var result = ChannelRetoucher.Stamp(
            image,
            new CloneStampStroke(
                SourceAnchor: new RetouchPoint(4, 8),
                DestinationStroke: new RetouchStroke([new RetouchPoint(16, 8)], BrushSize: 7),
                BlendWidth: 3));

        result.Image[16, 8].Should().BeApproximately(0.9f, 0.02f);
        result.Image[13, 8].Should().BeGreaterThan(0.1f);
        result.Image[13, 8].Should().BeLessThan(0.75f);
    }

    [Fact]
    public void Stamp_LeavesOutOfBoundsSourceSamplesUnchanged()
    {
        var image = ImageBuffer.Filled(12, 12, 0.1f);
        image[0, 0] = 0.9f;

        var result = ChannelRetoucher.Stamp(
            image,
            new CloneStampStroke(
                SourceAnchor: new RetouchPoint(0, 0),
                DestinationStroke: new RetouchStroke([new RetouchPoint(4, 4)], BrushSize: 7),
                BlendWidth: 1));

        result.Image[4, 4].Should().BeApproximately(0.9f, 0.001f);
        result.Image[1, 4].Should().BeApproximately(0.1f, 0.001f);
        result.Mask[(4 * image.Width) + 1].Should().Be(0);
    }

    [Fact]
    public void Stamp_SourceMaskLimitsCopiedPixels()
    {
        var image = ImageBuffer.Filled(16, 12, 0.1f);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                image[x, y] = 0.9f;
            }
        }

        var result = ChannelRetoucher.Stamp(
            image,
            new CloneStampStroke(
                SourceAnchor: new RetouchPoint(2, 6),
                DestinationStroke: new RetouchStroke([new RetouchPoint(10, 6)], BrushSize: 5),
                BlendWidth: 1,
                SourceMaskStroke: new RetouchStroke([new RetouchPoint(2, 6)], BrushSize: 1)));

        result.Image[10, 6].Should().BeApproximately(0.9f, 0.001f);
        result.Image[12, 6].Should().BeApproximately(0.1f, 0.001f);
        result.Mask[(6 * image.Width) + 12].Should().Be(0);
    }
}
