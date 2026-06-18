using FluentAssertions;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests;

public sealed class ImageLoaderTests
{
    [Fact]
    public async Task SaveGrayscalePngAsync_WritesReadableGrayscaleImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-channel-{Guid.NewGuid():N}.png");
        try
        {
            var image = new ImageBuffer(2, 2, [0.0f, 0.25f, 0.5f, 1.0f]);

            await ImageLoader.SaveGrayscalePngAsync(path, image);
            var loaded = await ImageLoader.LoadGrayscaleAsync(path);

            loaded.Width.Should().Be(2);
            loaded.Height.Should().Be(2);
            loaded[1, 1].Should().BeApproximately(1.0f, 0.01f);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(RgbExportFormat.Png, ".png")]
    [InlineData(RgbExportFormat.Jpeg, ".jpg")]
    [InlineData(RgbExportFormat.Tiff, ".tif")]
    public async Task SaveRgbAsync_WritesReadableImage(RgbExportFormat format, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-result-{Guid.NewGuid():N}{extension}");
        try
        {
            var rgb = new RgbImageBuffer(
                2,
                2,
                [
                    1.0f, 0.0f, 0.0f,
                    0.0f, 1.0f, 0.0f,
                    0.0f, 0.0f, 1.0f,
                    1.0f, 1.0f, 1.0f,
                ]);

            await ImageLoader.SaveRgbAsync(path, rgb, RgbExportSettings.Default with { Format = format });
            var loaded = await ImageLoader.LoadGrayscaleAsync(path);

            loaded.Width.Should().Be(2);
            loaded.Height.Should().Be(2);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SaveRgbAsync_ResizesToMaxSidePreservingAspectRatio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-result-{Guid.NewGuid():N}.png");
        try
        {
            var rgb = FilledRgb(8, 4, 0.5f);
            var settings = RgbExportSettings.Default with { MaxSide = 4 };

            await ImageLoader.SaveRgbAsync(path, rgb, settings);
            var loaded = await ImageLoader.LoadGrayscaleAsync(path);

            loaded.Width.Should().Be(4);
            loaded.Height.Should().Be(2);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static RgbImageBuffer FilledRgb(int width, int height, float value)
    {
        var pixels = new float[width * height * 3];
        Array.Fill(pixels, value);
        return new RgbImageBuffer(width, height, pixels);
    }
}
