using FluentAssertions;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests;

public sealed class RgbImageBufferTests
{
    [Fact]
    public void Crop_ExtractsSubregion()
    {
        var pixels = new float[3 * 2 * 3];
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var index = (((y * 3) + x) * 3);
                pixels[index] = x / 3.0f;
                pixels[index + 1] = y / 2.0f;
                pixels[index + 2] = 0.5f;
            }
        }

        var image = new RgbImageBuffer(3, 2, pixels);
        var cropped = image.Crop(1, 0, 2, 1);

        cropped.Width.Should().Be(2);
        cropped.Height.Should().Be(1);
        cropped[0, 0, 0].Should().BeApproximately(1 / 3.0f, 0.001f);
        cropped[1, 0, 0].Should().BeApproximately(2 / 3.0f, 0.001f);
        cropped[0, 0, 1].Should().BeApproximately(0.0f, 0.001f);
        cropped[0, 0, 2].Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void Crop_OutsideBounds_Throws()
    {
        var image = new RgbImageBuffer(2, 2, new float[2 * 2 * 3]);
        var act = () => image.Crop(1, 1, 2, 2);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
