using FluentAssertions;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests;

public sealed class CrossChannelHealerTests
{
    [Fact]
    public void HealChannel_RepairsRedOnlyDefectWithoutChangingGuides()
    {
        var red = CreateCorrelatedChannels(31, 31, 0.5f);
        var green = CreateCorrelatedChannels(31, 31, 0.5f);
        var blue = CreateCorrelatedChannels(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        var greenBefore = green.Clone();
        var blueBefore = blue.Clone();
        var mask = DefectMask(red, 15, 15);

        var result = ChannelHealer.HealChannel(
            red,
            green,
            blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided, PatchRadius: 3, SearchRadius: 24));

        result.Image[15, 15].Should().BeLessThan(0.85f);
        green.Pixels.Should().Equal(greenBefore.Pixels);
        blue.Pixels.Should().Equal(blueBefore.Pixels);
        MergeRgb(result.Image, green, blue)[15, 15, 0].Should().BeLessThan(0.85f);
    }

    [Fact]
    public void HealChannel_RepairsGreenOnlyDefect()
    {
        var red = CreateCorrelatedChannels(31, 31, 0.5f);
        var green = CreateCorrelatedChannels(31, 31, 0.5f);
        var blue = CreateCorrelatedChannels(31, 31, 0.5f);
        green[12, 12] = 0.0f;
        var mask = DefectMask(green, 12, 12);

        var result = ChannelHealer.HealChannel(
            green,
            red,
            blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided, PatchRadius: 3, SearchRadius: 24));

        result.Image[12, 12].Should().BeGreaterThan(0.15f);
        MergeRgb(red, result.Image, blue)[12, 12, 1].Should().BeGreaterThan(0.15f);
    }

    [Fact]
    public void HealChannel_ReportsMonotonicProgressToCompletion()
    {
        var red = CreateCorrelatedChannels(31, 31, 0.5f);
        var green = CreateCorrelatedChannels(31, 31, 0.5f);
        var blue = CreateCorrelatedChannels(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        var mask = DefectMask(red, 15, 15);
        var progress = new ProgressCapture();

        ChannelHealer.HealChannel(
            red,
            green,
            blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided, PatchRadius: 3, SearchRadius: 24),
            progress);

        progress.Values.Should().NotBeEmpty();
        progress.Values[0].Should().Be(0.0);
        progress.Values[^1].Should().Be(100.0);
        progress.Values.Should().OnlyContain(value => value >= 0.0 && value <= 100.0);
        for (var i = 1; i < progress.Values.Count; i++)
        {
            progress.Values[i].Should().BeGreaterThanOrEqualTo(progress.Values[i - 1]);
        }
    }

    [Fact]
    public void HealChannel_PreservesSaturatedRedObject()
    {
        var red = ImageBuffer.Filled(25, 25, 0.5f);
        var green = ImageBuffer.Filled(25, 25, 0.5f);
        var blue = ImageBuffer.Filled(25, 25, 0.5f);
        for (var y = 8; y < 17; y++)
        {
            for (var x = 8; x < 17; x++)
            {
                red[x, y] = 0.95f;
                green[x, y] = 0.20f;
                blue[x, y] = 0.20f;
            }
        }

        red[10, 10] = 1.0f;
        var mask = DefectMask(red, 10, 10, brushSize: 3);
        var result = ChannelHealer.HealChannel(
            red,
            green,
            blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided, PatchRadius: 3, SearchRadius: 24));

        result.Image[10, 10].Should().BeGreaterThan(0.65f);
        result.Image[10, 10].Should().BeLessThan(1.0f);
    }

    [Fact]
    public void HealChannel_PreservesEdgeNearImageBorder()
    {
        var red = CreateCorrelatedChannels(21, 21, 0.5f);
        var green = CreateCorrelatedChannels(21, 21, 0.5f);
        var blue = CreateCorrelatedChannels(21, 21, 0.5f);
        red[1, 1] = 1.0f;
        var mask = DefectMask(red, 1, 1);

        var act = () => ChannelHealer.HealChannel(
            red,
            green,
            blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided, PatchRadius: 3, SearchRadius: 24));

        act.Should().NotThrow();
    }

    [Fact]
    public void HealChannel_LeavesPixelsOutsideMaskUnchanged()
    {
        var red = CreateCorrelatedChannels(17, 17, 0.5f);
        var green = CreateCorrelatedChannels(17, 17, 0.5f);
        var blue = CreateCorrelatedChannels(17, 17, 0.5f);
        red[8, 8] = 1.0f;
        var before = red.Clone();
        var mask = DefectMask(red, 8, 8);

        var result = ChannelHealer.HealChannel(
            red,
            green,
            blue,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided, PatchRadius: 3, SearchRadius: 24));

        result.Image[0, 0].Should().BeApproximately(before[0, 0], 0.001f);
        result.Image[16, 16].Should().BeApproximately(before[16, 16], 0.001f);
    }

    [Fact]
    public void HealChannel_UsesBulkPredictionForLargeAutoCleanMasks()
    {
        var red = CreateCorrelatedChannels(17, 17, 0.5f);
        var green = CreateCorrelatedChannels(17, 17, 0.5f);
        var blue = CreateCorrelatedChannels(17, 17, 0.5f);
        var mask = new byte[red.PixelCount];
        for (var i = 0; i < mask.Length; i += 17)
        {
            red.SetNormalized(i, 1.0f);
            mask[i] = 1;
        }

        var result = ChannelHealer.HealChannel(
            red,
            green,
            blue,
            mask,
            new HealOptions(
                Mode: HealingMode.CrossChannelGuided,
                PatchRadius: 3,
                LargeMaskFastPathPixelThreshold: 8));

        result.UsedCrossChannel.Should().BeTrue();
        result.StatusMessage.Should().Contain("bulk");
        result.Image.GetNormalized(0).Should().BeApproximately(0.5f, 0.02f);
        result.Image.GetNormalized(1).Should().BeApproximately(red.GetNormalized(1), 0.001f);
    }

    [Fact]
    public void HealChannel_FallsBackToTeleaWhenGuidesMissing()
    {
        var red = ImageBuffer.Filled(11, 11, 0.5f);
        red[5, 5] = 1.0f;
        var mask = DefectMask(red, 5, 5);

        var result = ChannelHealer.HealChannel(
            red,
            null,
            null,
            mask,
            new HealOptions(Mode: HealingMode.CrossChannelGuided));

        result.UsedFallback.Should().BeTrue();
        result.StatusMessage.Should().Contain("Telea");
        result.Image[5, 5].Should().BeLessThan(0.9f);
    }

    private static ImageBuffer CreateCorrelatedChannels(int width, int height, float value)
    {
        return ImageBuffer.Filled(width, height, value);
    }

    private static byte[] DefectMask(ImageBuffer image, int x, int y, int brushSize = 7)
    {
        return ChannelRetoucher.CreateBrushMask(
            image.Width,
            image.Height,
            [new RetouchStroke([new RetouchPoint(x, y)], brushSize)]);
    }

    private static RgbImageBuffer MergeRgb(ImageBuffer red, ImageBuffer green, ImageBuffer blue)
    {
        var mask = Enumerable.Repeat((byte)1, red.PixelCount).ToArray();
        return Cropper.MergeChannels(red, green, blue, mask, mask, mask).Rgb;
    }

    private sealed class ProgressCapture : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value)
        {
            Values.Add(value);
        }
    }
}
