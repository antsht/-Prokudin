using FluentAssertions;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests;

public sealed class TriptychSplitterTests
{
    [Fact]
    public void SplitTriptych_SplitsHorizontalRgbOrder()
    {
        var image = HorizontalTriptych(300, 100, 0.2f, 0.5f, 0.8f);

        var channels = TriptychSplitter.SplitTriptych(image, TriptychOrder.Rgb, trimBlackBorders: false);

        channels[ChannelName.Red].Width.Should().Be(100);
        channels[ChannelName.Red].Height.Should().Be(100);
        Mean(channels[ChannelName.Red]).Should().BeApproximately(0.2f, 0.001f);
        Mean(channels[ChannelName.Green]).Should().BeApproximately(0.5f, 0.001f);
        Mean(channels[ChannelName.Blue]).Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void SplitTriptych_SplitsVerticalBgrOrder()
    {
        var image = VerticalTriptych(100, 300, 0.2f, 0.5f, 0.8f);

        var channels = TriptychSplitter.SplitTriptych(image, TriptychOrder.Bgr, trimBlackBorders: false);

        channels[ChannelName.Red].Width.Should().Be(100);
        channels[ChannelName.Red].Height.Should().Be(100);
        Mean(channels[ChannelName.Blue]).Should().BeApproximately(0.2f, 0.001f);
        Mean(channels[ChannelName.Green]).Should().BeApproximately(0.5f, 0.001f);
        Mean(channels[ChannelName.Red]).Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void SplitTriptych_LastSegmentKeepsRemainderPixels()
    {
        var image = HorizontalTriptych(302, 20, 0.1f, 0.4f, 0.7f);

        var channels = TriptychSplitter.SplitTriptych(image, TriptychOrder.Rgb, trimBlackBorders: false);

        channels[ChannelName.Red].Width.Should().Be(100);
        channels[ChannelName.Green].Width.Should().Be(100);
        channels[ChannelName.Blue].Width.Should().Be(102);
    }

    private static ImageBuffer HorizontalTriptych(int width, int height, params float[] values)
    {
        var image = ImageBuffer.Filled(width, height, 0.0f);
        var third = width / 3;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = values[Math.Min(x / third, 2)];
            }
        }

        return image;
    }

    private static ImageBuffer VerticalTriptych(int width, int height, params float[] values)
    {
        var image = ImageBuffer.Filled(width, height, 0.0f);
        var third = height / 3;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = values[Math.Min(y / third, 2)];
            }
        }

        return image;
    }

    private static float Mean(ImageBuffer image)
    {
        return image.Pixels.Average();
    }
}
