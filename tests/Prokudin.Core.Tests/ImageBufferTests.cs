using FluentAssertions;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests;

public sealed class ImageBufferTests
{
    [Fact]
    public void FloatBuffer_PreservesDirectPixelAccess()
    {
        var image = ImageBuffer.Filled(4, 4, 0.5f);
        image.Pixels[0].Should().BeApproximately(0.5f, 0.001f);
        image.Format.Should().Be(PixelFormat.Float32);
    }

    [Fact]
    public void UInt8Buffer_RoundTripsNormalizedValues()
    {
        var image = ImageBuffer.Filled(3, 3, 0.5f, PixelFormat.UInt8);
        image.GetNormalized(0).Should().BeApproximately(0.5f, 0.01f);
        image[1, 1] = 0.75f;
        image.GetNormalized((1 * image.Width) + 1).Should().BeApproximately(0.75f, 0.02f);
    }

    [Fact]
    public void UInt16Buffer_ConvertsToFloat()
    {
        var pixels = new ushort[] { 0, 32768, 65535 };
        var image = new ImageBuffer(3, 1, pixels);
        image.WithFormat(PixelFormat.Float32).GetNormalized(2).Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void Crop_PreservesFormat()
    {
        var image = ImageBuffer.Filled(8, 8, 0.25f, PixelFormat.UInt8);
        image.Crop(1, 1, 4, 4).Format.Should().Be(PixelFormat.UInt8);
    }
}
