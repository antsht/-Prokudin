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
    public void DetectSingleChannelDefects_FindsBrightAndDarkTargetOnlyDefects()
    {
        var target = ImageBuffer.Filled(31, 31, 0.5f);
        var other1 = ImageBuffer.Filled(31, 31, 0.5f);
        var other2 = ImageBuffer.Filled(31, 31, 0.5f);
        target[9, 9] = 1.0f;
        target[21, 21] = 0.0f;

        var result = ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings(Sensitivity: 65, InpaintRadius: 3));

        result.CandidatePixels.Should().BeGreaterThan(0);
        result.Mask[(9 * target.Width) + 9].Should().Be(1);
        result.Mask[(21 * target.Width) + 21].Should().Be(1);
    }

    [Fact]
    public void DetectSingleChannelDefects_HighSensitivityFindsSubtleTargetOnlyDefects()
    {
        var target = ImageBuffer.Filled(41, 41, 0.5f);
        var other1 = ImageBuffer.Filled(41, 41, 0.5f);
        var other2 = ImageBuffer.Filled(41, 41, 0.5f);
        target[14, 14] = 0.57f;
        target[26, 26] = 0.43f;

        var lowSensitivity = ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings(Sensitivity: 20, InpaintRadius: 3));
        var highSensitivity = ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings(Sensitivity: 100, InpaintRadius: 3));

        lowSensitivity.Mask[(14 * target.Width) + 14].Should().Be(0);
        lowSensitivity.Mask[(26 * target.Width) + 26].Should().Be(0);
        highSensitivity.Mask[(14 * target.Width) + 14].Should().Be(1);
        highSensitivity.Mask[(26 * target.Width) + 26].Should().Be(1);
        highSensitivity.CandidatePixels.Should().BeGreaterThan(lowSensitivity.CandidatePixels);
    }

    [Fact]
    public void DetectSingleChannelDefects_DoesNotFlagSharedEdges()
    {
        var target = SharedEdgeImage();
        var other1 = SharedEdgeImage();
        var other2 = SharedEdgeImage();

        var result = ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings(Sensitivity: 80, InpaintRadius: 3));

        result.CandidatePixels.Should().Be(0);
        result.Mask.Should().OnlyContain(value => value == 0);
    }

    [Fact]
    public void DetectSingleChannelDefects_RejectsMismatchedSizes()
    {
        var target = ImageBuffer.Filled(12, 12, 0.5f);
        var other1 = ImageBuffer.Filled(11, 12, 0.5f);
        var other2 = ImageBuffer.Filled(12, 12, 0.5f);

        var act = () => ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*same dimensions*");
    }

    [Fact]
    public void InpaintMask_WithDetectedCrossChannelMask_RepairsOnlyTargetChannel()
    {
        var target = ImageBuffer.Filled(31, 31, 0.5f);
        var other1 = ImageBuffer.Filled(31, 31, 0.5f);
        var other2 = ImageBuffer.Filled(31, 31, 0.5f);
        target[15, 15] = 1.0f;

        var mask = ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings(Sensitivity: 65, InpaintRadius: 3));

        var repaired = ChannelRetoucher.InpaintMask(target, mask.Mask, radius: 3);

        repaired[15, 15].Should().BeLessThan(0.85f);
        other1[15, 15].Should().BeApproximately(0.5f, 0.001f);
        other2[15, 15].Should().BeApproximately(0.5f, 0.001f);
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

    private static ImageBuffer SharedEdgeImage()
    {
        var image = ImageBuffer.Filled(31, 31, 0.35f);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 16; x < image.Width; x++)
            {
                image[x, y] = 0.75f;
            }
        }

        return image;
    }
}
