using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Core.Tests;

public sealed class CropperTests
{
    [Fact]
    public void MergeChannels_UsesGrayscaleForPartialOverlapPixels()
    {
        var red = new ImageBuffer(2, 1, [0.8f, 1.0f]);
        var green = new ImageBuffer(2, 1, [0.2f, 0.0f]);
        var blue = new ImageBuffer(2, 1, [0.0f, 0.25f]);
        var maskRed = new byte[] { 1, 1 };
        var maskGreen = new byte[] { 1, 0 };
        var maskBlue = new byte[] { 0, 0 };

        var (rgb, overlap) = Cropper.MergeChannels(red, green, blue, maskRed, maskGreen, maskBlue);

        overlap.Should().Equal((byte)0, (byte)0);
        rgb[0, 0, 0].Should().BeApproximately(0.402f, 0.001f);
        rgb[0, 0, 1].Should().BeApproximately(0.402f, 0.001f);
        rgb[0, 0, 2].Should().BeApproximately(0.402f, 0.001f);
        rgb[1, 0, 0].Should().BeApproximately(1.0f, 0.001f);
        rgb[1, 0, 1].Should().BeApproximately(1.0f, 0.001f);
        rgb[1, 0, 2].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void CropToContent_PreservesAspectRatioAndTrimsBlackBorders()
    {
        var pixels = new float[10 * 6 * 3];
        for (var y = 1; y < 5; y++)
        {
            for (var x = 2; x < 9; x++)
            {
                var index = (((y * 10) + x) * 3);
                pixels[index] = 0.5f;
                pixels[index + 1] = 0.5f;
                pixels[index + 2] = 0.5f;
            }
        }

        var rgb = new RgbImageBuffer(10, 6, pixels);
        var overlap = new byte[10 * 6];
        for (var y = 1; y < 5; y++)
        {
            for (var x = 3; x < 8; x++)
            {
                overlap[(y * 10) + x] = 1;
            }
        }

        var (cropped, info) = Cropper.CropToContent(rgb, overlap);

        cropped.Width.Should().Be(7);
        cropped.Height.Should().Be(4);
        info.X0.Should().Be(2);
        info.Y0.Should().Be(1);
        info.X1.Should().Be(9);
        info.Y1.Should().Be(5);
        info.OverlapX0.Should().Be(3);
        info.OverlapY0.Should().Be(1);
        info.OverlapX1.Should().Be(8);
        info.OverlapY1.Should().Be(5);
    }

    [Fact]
    public void LargestFullOverlapRectangle_StaysInsideCommonMask()
    {
        const int width = 5;
        const int height = 4;
        var red = Enumerable.Repeat((byte)1, width * height).ToArray();
        var green = Enumerable.Repeat((byte)1, width * height).ToArray();
        var blue = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            blue[(y * width) + 1] = 1;
            blue[(y * width) + 2] = 1;
        }

        blue[3] = 1;
        blue[width + 3] = 1;

        var crop = Cropper.LargestFullOverlapRectangle(red, green, blue, width, height);

        crop.Should().Be((1, 0, 3, 4));
    }

    [Fact]
    public void CropToLargestFullOverlap_CropsAlignedChannelsAndMasks()
    {
        const int width = 5;
        const int height = 4;
        var fullMask = Enumerable.Repeat((byte)1, width * height).ToArray();
        var blueMask = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            blueMask[(y * width) + 1] = 1;
            blueMask[(y * width) + 2] = 1;
        }

        var aligned = new AlignedChannels(
            NumberedChannel(width, height),
            ImageBuffer.Filled(width, height, 0.5f),
            ImageBuffer.Filled(width, height, 0.75f),
            fullMask,
            fullMask,
            blueMask,
            new Dictionary<ChannelName, AlignChannelMetadata>());

        var (cropped, info) = AlignedChannelCropper.CropToLargestFullOverlap(aligned);

        cropped.Red.Width.Should().Be(2);
        cropped.Red.Height.Should().Be(4);
        cropped.MaskRed.Should().OnlyContain(value => value == 1);
        cropped.MaskGreen.Should().OnlyContain(value => value == 1);
        cropped.MaskBlue.Should().OnlyContain(value => value == 1);
        cropped.Red[0, 0].Should().BeApproximately(1 / 20.0f, 0.001f);
        info.Should().Be(new CropInfo(1, 0, 3, 4, 1, 0, 3, 4));
    }

    private static ImageBuffer NumberedChannel(int width, int height)
    {
        var pixels = new float[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i / (float)pixels.Length;
        }

        return new ImageBuffer(width, height, pixels);
    }
}
