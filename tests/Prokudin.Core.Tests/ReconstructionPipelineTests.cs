using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests;

public sealed class ReconstructionPipelineTests
{
    [Fact]
    public void RunAutoAlign_PreparedChannelsBuildMatchesLegacyResultSize()
    {
        var green = SyntheticChannel();
        var red = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: 6, dy: -4).Image;
        var blue = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: -5, dy: 7).Image;
        var channels = new Dictionary<ChannelName, ImageBuffer>
        {
            [ChannelName.Red] = red,
            [ChannelName.Green] = green,
            [ChannelName.Blue] = blue,
        };
        var settings = new PipelineSettings { Align = new AlignOptions(MaxTranslation: 12) };

        var aligned = ReconstructionPipeline.RunAutoAlign(channels, settings.Align);
        var (legacyRgb, _) = ReconstructionPipeline.BuildRgb(aligned, settings);
        var prepared = AlignedChannelCropper.CropToLargestFullOverlap(aligned);
        var (guiRgb, _) = ReconstructionPipeline.BuildRgb(prepared.Channels, settings);

        guiRgb.Width.Should().Be(legacyRgb.Width);
        guiRgb.Height.Should().Be(legacyRgb.Height);
    }

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

        rgb.Width.Should().BeGreaterThanOrEqualTo(80);
        rgb.Height.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public void AlignThenBrushHeal_UInt16_DoesNotBlackenGreenChannel()
    {
        var green = SyntheticChannelUInt16();
        var red = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: 6, dy: -4).Image;
        var blue = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: -5, dy: 7).Image;
        var settings = new PipelineSettings { Align = new AlignOptions(MaxTranslation: 12) };
        var aligned = ReconstructionPipeline.RunAutoAlign(
            new Dictionary<ChannelName, ImageBuffer>
            {
                [ChannelName.Red] = red,
                [ChannelName.Green] = green,
                [ChannelName.Blue] = blue,
            },
            settings.Align);
        var prepared = AlignedChannelCropper.CropToLargestFullOverlap(aligned).Channels;
        MeanNormalized(prepared.Green).Should().BeGreaterThan(0.05f);

        var mask = ChannelRetoucher.CreateBrushMask(
            prepared.Green.Width,
            prepared.Green.Height,
            [new RetouchStroke([new RetouchPoint(64, 64)], BrushSize: 16)]);
        var healed = ChannelHealer.HealChannel(
            prepared.Green,
            prepared.Red,
            prepared.Blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided));

        MeanNormalized(healed.Image).Should().BeGreaterThan(0.05f);
    }

    [Fact]
    public void BuildRgb_WithSkipCropPreservesPreparedChannelSize()
    {
        const int width = 5;
        const int height = 3;
        var mask = Enumerable.Repeat((byte)1, width * height).ToArray();
        var aligned = new AlignedChannels(
            ChannelWithBlackColumns(width, height),
            ChannelWithBlackColumns(width, height),
            ChannelWithBlackColumns(width, height),
            mask,
            mask,
            mask,
            new Dictionary<ChannelName, AlignChannelMetadata>());

        var (rgb, cropInfo) = ReconstructionPipeline.BuildRgb(
            aligned,
            new PipelineSettings
            {
                Color = new ColorSettings(AutoWhiteBalance: false),
                Crop = new CropSettings { SkipCrop = true },
                Sharpen = false,
            });

        rgb.Width.Should().Be(width);
        rgb.Height.Should().Be(height);
        cropInfo.Should().Be(new CropInfo(0, 0, width, height, 0, 0, width, height));
    }

    private static float MeanNormalized(ImageBuffer image)
    {
        var sum = 0.0f;
        for (var i = 0; i < image.PixelCount; i++)
        {
            sum += image.GetNormalized(i);
        }

        return sum / image.PixelCount;
    }

    private static ImageBuffer SyntheticChannelUInt16()
    {
        var image = ImageBuffer.Filled(128, 128, 0.0f, PixelFormat.UInt16);
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

    private static ImageBuffer ChannelWithBlackColumns(int width, int height)
    {
        var image = ImageBuffer.Filled(width, height, 0.0f);
        for (var y = 0; y < height; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                image[x, y] = 0.5f;
            }
        }

        return image;
    }
}
