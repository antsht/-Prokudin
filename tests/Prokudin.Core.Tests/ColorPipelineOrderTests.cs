using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Core.Tests;

public sealed class ColorPipelineOrderTests
{
    [Fact]
    public void BuildRgb_AppliesColourStagesInApprovedOrder()
    {
        var mask = Enumerable.Repeat((byte)1, 4).ToArray();
        var aligned = new AlignedChannels(
            ImageBuffer.Filled(2, 2, 0.2f),
            ImageBuffer.Filled(2, 2, 0.4f),
            ImageBuffer.Filled(2, 2, 0.8f),
            mask,
            mask,
            mask,
            new Dictionary<ChannelName, AlignChannelMetadata>());

        var (rgb, _) = ReconstructionPipeline.BuildRgb(
            aligned,
            new PipelineSettings
            {
                Crop = new CropSettings { SkipCrop = true },
                Exposure = new ChannelExposureSettings(RedStops: 1),
                Color = new ColorSettings(
                    WhiteBalanceSource.WhitePick,
                    Temperature: 100,
                    Tint: 100,
                    WhitePick: new WhitePick(0, 0)),
                ChannelLevels = new ChannelLevelsSettings(
                    Blue: new ChannelLevelSettings(WhitePoint: 0.75f)),
                Levels = new LevelsSettings(
                    Mode: LevelsMode.Manual,
                    BlackPoint: 0.5f,
                    WhitePoint: 1.0f,
                    Gamma: 1.0f),
                Sharpen = false,
            });

        rgb[0, 0, 0].Should().BeApproximately(1.0f, 0.001f);
        rgb[0, 0, 1].Should().BeApproximately(0.84f, 0.001f);
        rgb[0, 0, 2].Should().BeApproximately(0.60f, 0.001f);
        aligned.Red[0, 0].Should().BeApproximately(0.2f, 0.001f);
    }
}
