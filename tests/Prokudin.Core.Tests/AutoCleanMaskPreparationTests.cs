using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests;

public sealed class AutoCleanMaskPreparationTests
{
    [Fact]
    public void PrepareAutoCleanMask_LeavesMaskUnchangedWhenMergeAndExpandDisabled()
    {
        var raw = SinglePixelMask(15, 15, 7, 7);

        var result = ChannelRetoucher.PrepareAutoCleanMask(
            raw,
            width: 15,
            height: 15,
            new AutoCleanSettings(
                AutoExpandHealingAreaPx: 0,
                AutoMergeNearbyDefects: false));

        result.Mask.Should().Equal(raw);
        result.RawMask.Should().Equal(raw);
        result.MergedMask.Should().Equal(raw);
        result.ExpandedMask.Should().Equal(raw);
        result.FinalMask.Should().Equal(raw);
    }

    [Fact]
    public void PrepareAutoCleanMask_ExpandsFinalHealingMaskByConfiguredRadius()
    {
        var raw = SinglePixelMask(15, 15, 7, 7);

        var result = ChannelRetoucher.PrepareAutoCleanMask(
            raw,
            width: 15,
            height: 15,
            new AutoCleanSettings(
                AutoExpandHealingAreaPx: 3,
                AutoMergeNearbyDefects: false));

        result.FinalMask[(7 * 15) + 7].Should().Be(1);
        result.FinalMask[(7 * 15) + 4].Should().Be(1);
        result.FinalMask[(7 * 15) + 10].Should().Be(1);
        result.FinalMask[(4 * 15) + 7].Should().Be(1);
        result.FinalMask[(10 * 15) + 7].Should().Be(1);
        result.FinalMask[(7 * 15) + 3].Should().Be(0);
        result.FinalMask.Count(value => value > 0).Should().BeGreaterThan(raw.Count(value => value > 0));
    }

    [Fact]
    public void PrepareAutoCleanMask_MergesNearbyDotsWhenEnabled()
    {
        var raw = new byte[21 * 21];
        raw[(10 * 21) + 9] = 1;
        raw[(10 * 21) + 11] = 1;

        var result = ChannelRetoucher.PrepareAutoCleanMask(
            raw,
            width: 21,
            height: 21,
            new AutoCleanSettings(
                AutoExpandHealingAreaPx: 0,
                AutoMergeNearbyDefects: true,
                AutoMergeDistancePx: 3));

        CountComponents(result.FinalMask, 21, 21).Should().Be(1);
    }

    [Fact]
    public void PrepareAutoCleanMask_KeepsNearbyDotsSeparateWhenMergeDisabled()
    {
        var raw = new byte[21 * 21];
        raw[(10 * 21) + 9] = 1;
        raw[(10 * 21) + 11] = 1;

        var result = ChannelRetoucher.PrepareAutoCleanMask(
            raw,
            width: 21,
            height: 21,
            new AutoCleanSettings(
                AutoExpandHealingAreaPx: 0,
                AutoMergeNearbyDefects: false,
                AutoMergeDistancePx: 3));

        CountComponents(result.FinalMask, 21, 21).Should().Be(2);
    }

    [Fact]
    public void PrepareAutoCleanMask_DoesNotMergeDistantDots()
    {
        var raw = new byte[31 * 31];
        raw[(15 * 31) + 5] = 1;
        raw[(15 * 31) + 25] = 1;

        var result = ChannelRetoucher.PrepareAutoCleanMask(
            raw,
            width: 31,
            height: 31,
            new AutoCleanSettings(
                AutoExpandHealingAreaPx: 0,
                AutoMergeNearbyDefects: true,
                AutoMergeDistancePx: 3));

        CountComponents(result.FinalMask, 31, 31).Should().Be(2);
    }

    [Fact]
    public void PrepareAutoCleanMask_ReducesRadiiWhenPreparedComponentIsTooLarge()
    {
        var raw = SinglePixelMask(31, 31, 15, 15);

        var result = ChannelRetoucher.PrepareAutoCleanMask(
            raw,
            width: 31,
            height: 31,
            new AutoCleanSettings(
                AutoExpandHealingAreaPx: 10,
                AutoMergeNearbyDefects: false,
                MaxAutoExpandedComponentArea: 9));

        result.FinalMask.Count(value => value > 0).Should().BeLessThan(20);
        result.FinalMask.Count(value => value > 0).Should().BeGreaterThan(0);
    }

    [Fact]
    public void DetectSingleChannelDefects_ReturnsPreparedMaskStagesAndDebugFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = ImageBuffer.Filled(31, 31, 0.5f);
            var other1 = ImageBuffer.Filled(31, 31, 0.5f);
            var other2 = ImageBuffer.Filled(31, 31, 0.5f);
            target[15, 15] = 1.0f;

            var result = ChannelRetoucher.DetectSingleChannelDefects(
                target,
                other1,
                other2,
                new AutoCleanSettings(
                    Sensitivity: 65,
                    InpaintRadius: 3,
                    AutoExpandHealingAreaPx: 2,
                    AutoMergeNearbyDefects: true,
                    AutoMergeDistancePx: 3,
                    DebugOutput: true,
                    DebugOutputDirectory: directory,
                    DebugMaskPrefix: "R_"));

            result.Mask.Should().Equal(result.FinalMask);
            result.CandidatePixels.Should().Be(result.FinalMask.Count(value => value > 0));
            File.Exists(Path.Combine(directory, "R_auto_defect_mask_raw.png")).Should().BeTrue();
            File.Exists(Path.Combine(directory, "R_auto_defect_mask_merged.png")).Should().BeTrue();
            File.Exists(Path.Combine(directory, "R_auto_defect_mask_expanded.png")).Should().BeTrue();
            File.Exists(Path.Combine(directory, "R_final_healing_mask.png")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DetectSingleChannelDefects_PreparesSmallGuiAutoCleanMaskQuickly()
    {
        var target = ImageBuffer.Filled(21, 21, 0.5f);
        var other1 = ImageBuffer.Filled(21, 21, 0.5f);
        var other2 = ImageBuffer.Filled(21, 21, 0.5f);
        target[10, 10] = 1.0f;

        var result = ChannelRetoucher.DetectSingleChannelDefects(
            target,
            other1,
            other2,
            new AutoCleanSettings(Sensitivity: 50, InpaintRadius: 3));

        result.CandidatePixels.Should().BeGreaterThan(0);
        result.Mask.Should().Equal(result.FinalMask);
    }

    private static byte[] SinglePixelMask(int width, int height, int x, int y)
    {
        var mask = new byte[width * height];
        mask[(y * width) + x] = 1;
        return mask;
    }

    private static int CountComponents(byte[] mask, int width, int height)
    {
        var seen = new bool[mask.Length];
        var count = 0;
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0 || seen[i])
            {
                continue;
            }

            count++;
            Flood(mask, seen, width, height, i);
        }

        return count;
    }

    private static void Flood(byte[] mask, bool[] seen, int width, int height, int start)
    {
        var queue = new Queue<int>();
        queue.Enqueue(start);
        seen[start] = true;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var x = current % width;
            var y = current / width;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    var index = (ny * width) + nx;
                    if (seen[index] || mask[index] == 0)
                    {
                        continue;
                    }

                    seen[index] = true;
                    queue.Enqueue(index);
                }
            }
        }
    }
}
