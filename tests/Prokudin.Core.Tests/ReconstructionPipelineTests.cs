using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Core.Tests;

public sealed class ReconstructionPipelineTests
{
    [Fact]
    public void RunAutoAlign_AlignsSyntheticTranslations()
    {
        var green = SyntheticChannel();
        var red = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: 6, dy: -4).Image;
        var blue = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: -5, dy: 7).Image;

        var aligned = ReconstructionPipeline.RunAutoAlign(
            new Dictionary<ChannelName, ImageBuffer>
            {
                [ChannelName.Red] = red,
                [ChannelName.Green] = green,
                [ChannelName.Blue] = blue,
            },
            new AlignOptions(MaxTranslation: 12));

        var (rgb, _) = ReconstructionPipeline.BuildRgb(aligned, new PipelineSettings { Align = new AlignOptions(MaxTranslation: 12) });

        rgb.Width.Should().Be(rgb.Height);
        rgb.Width.Should().BeGreaterThan(80);
    }

    private static ImageBuffer SyntheticChannel()
    {
        var image = ImageBuffer.Filled(128, 128, 0.0f);
        for (var y = 20; y < 108; y++)
        {
            for (var x = 24; x < 104; x++)
            {
                image[x, y] = 0.25f;
            }
        }

        for (var y = 48; y < 80; y++)
        {
            for (var x = 54; x < 86; x++)
            {
                image[x, y] = 0.85f;
            }
        }

        return image;
    }
}
