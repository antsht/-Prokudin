using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Core.Tests;

public sealed class ChannelExposureTests
{
    [Fact]
    public void BuildRgb_AppliesChannelExposureBeforeMerge()
    {
        var aligned = SolidAlignedChannels(red: 0.25f, green: 0.25f, blue: 0.25f);

        var (rgb, _) = ReconstructionPipeline.BuildRgb(
            aligned,
            new PipelineSettings
            {
                Color = new ColorSettings(AutoWhiteBalance: false),
                Exposure = new ChannelExposureSettings(RedStops: 1.0f),
                Sharpen = false,
            });

        rgb[0, 0, 0].Should().BeApproximately(0.5f, 0.001f);
        rgb[0, 0, 1].Should().BeApproximately(0.25f, 0.001f);
        rgb[0, 0, 2].Should().BeApproximately(0.25f, 0.001f);
    }

    [Fact]
    public void BuildRgb_RespectsAutoWhiteBalanceToggleWhenExposureChanges()
    {
        var aligned = SolidAlignedChannels(red: 0.2f, green: 0.4f, blue: 0.8f);

        var (rgb, _) = ReconstructionPipeline.BuildRgb(
            aligned,
            new PipelineSettings
            {
                Color = new ColorSettings(AutoWhiteBalance: false),
                Exposure = new ChannelExposureSettings(BlueStops: -1.0f),
                Sharpen = false,
            });

        rgb[0, 0, 0].Should().BeApproximately(0.2f, 0.001f);
        rgb[0, 0, 1].Should().BeApproximately(0.4f, 0.001f);
        rgb[0, 0, 2].Should().BeApproximately(0.4f, 0.001f);
    }

    private static AlignedChannels SolidAlignedChannels(float red, float green, float blue)
    {
        const int width = 4;
        const int height = 4;
        var mask = Enumerable.Repeat((byte)1, width * height).ToArray();
        return new AlignedChannels(
            ImageBuffer.Filled(width, height, red),
            ImageBuffer.Filled(width, height, green),
            ImageBuffer.Filled(width, height, blue),
            mask,
            mask,
            mask,
            new Dictionary<ChannelName, AlignChannelMetadata>());
    }
}
