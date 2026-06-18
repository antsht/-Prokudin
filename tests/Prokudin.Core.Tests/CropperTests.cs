using FluentAssertions;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;

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
}
