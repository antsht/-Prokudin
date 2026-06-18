using FluentAssertions;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests;

public sealed class CropperTests
{
    [Fact]
    public void SquareCrop_CropsCenteredSquareInsideOverlap()
    {
        var rgb = new RgbImageBuffer(10, 6, new float[10 * 6 * 3]);
        var overlap = new byte[10 * 6];
        for (var y = 1; y < 5; y++)
        {
            for (var x = 2; x < 9; x++)
            {
                overlap[(y * 10) + x] = 1;
            }
        }

        var (square, info) = Cropper.SquareCrop(rgb, overlap);

        square.Width.Should().Be(4);
        square.Height.Should().Be(4);
        info.OverlapX0.Should().Be(2);
        info.OverlapY0.Should().Be(1);
        info.OverlapX1.Should().Be(9);
        info.OverlapY1.Should().Be(5);
    }
}
