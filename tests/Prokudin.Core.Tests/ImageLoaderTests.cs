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
}
