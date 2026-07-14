using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests;

public sealed class GuidedHealingProvenanceTests
{
    [Fact]
    public void HealChannel_ProvenanceAwareBrushKeepsEveryUnmaskedNativeSampleExact()
    {
        const int width = 17;
        var pixels = Enumerable.Range(0, width * width).Select(i => (ushort)(12_000 + i)).ToArray();
        var target = new ImageBuffer(width, width, (ushort[])pixels.Clone());
        target.UInt16Pixels[8 * width + 8] = ushort.MaxValue;
        var mask = SingleMask(width, 8, 8);
        var result = Heal(target, CreateGuide(width, 0.31f), CreateGuide(width, 0.57f), mask);

        result.Image.Format.Should().Be(PixelFormat.UInt16);
        for (var index = 0; index < pixels.Length; index++)
        {
            if (mask[index] == 0)
            {
                result.Image.UInt16Pixels[index].Should().Be(target.UInt16Pixels[index]);
            }
        }
    }

    [Fact]
    public void HealChannel_ExcludesCloneStampedGuideAndMarksConservativeRepair()
    {
        const int size = 25;
        var target = Gradient(size, 0.18f, 0.45f);
        var guide1 = Gradient(size, 0.12f, 0.50f);
        var guide2 = Gradient(size, 0.10f, 0.35f);
        var center = (size / 2 * size) + size / 2;
        target.SetNormalized(center, 1.0f);
        var cloneProvenance = new RetouchProvenanceMap(size, size, RetouchProvenance.CloneStamp);
        var result = ChannelHealer.HealChannel(
            target,
            new HealingGuide(guide1, cloneProvenance),
            new HealingGuide(guide2, new RetouchProvenanceMap(size, size, RetouchProvenance.CloneStamp)),
            SingleMask(size, size / 2, size / 2),
            new RetouchProvenanceMap(size, size),
            new HealOptions());

        result.GuidedSummary!.ExcludedGuides.Should().Be(2);
        result.IsLowConfidence.Should().BeTrue();
        result.Provenance![center].Should().Be(RetouchProvenance.LowConfidenceHealing);
    }

    [Fact]
    public void HealChannel_UnknownGuideNeedsAgreementAndNeverActsAlone()
    {
        const int size = 25;
        var target = Gradient(size, 0.20f, 0.42f);
        var guide = Gradient(size, 0.10f, 0.51f);
        var second = Gradient(size, 0.15f, 0.35f);
        var center = (size / 2 * size) + size / 2;
        target.SetNormalized(center, 1.0f);
        var result = ChannelHealer.HealChannel(
            target,
            new HealingGuide(guide, RetouchProvenanceMap.Unknown(size, size)),
            new HealingGuide(second, new RetouchProvenanceMap(size, size, RetouchProvenance.CloneStamp)),
            SingleMask(size, size / 2, size / 2),
            new RetouchProvenanceMap(size, size),
            new HealOptions());

        result.IsLowConfidence.Should().BeTrue();
        result.Provenance![center].Should().Be(RetouchProvenance.LowConfidenceHealing);
    }

    [Fact]
    public void HealChannel_NeverReadsCloneStampedPixelAsGuideData()
    {
        const int size = 25;
        var target = Gradient(size, 0.20f, 0.42f);
        var guide1 = Gradient(size, 0.10f, 0.51f);
        var guide2 = Gradient(size, 0.15f, 0.35f);
        var center = (size / 2 * size) + size / 2;
        target.SetNormalized(center, 1.0f);
        guide1.SetNormalized(center, 1.0f);
        var guide1Provenance = new RetouchProvenanceMap(size, size);
        guide1Provenance[center] = RetouchProvenance.CloneStamp;
        var result = ChannelHealer.HealChannel(
            target,
            new HealingGuide(guide1, guide1Provenance),
            new HealingGuide(guide2, new RetouchProvenanceMap(size, size, RetouchProvenance.CloneStamp)),
            SingleMask(size, size / 2, size / 2),
            new RetouchProvenanceMap(size, size),
            new HealOptions(ContextRadius: 12, MinTrainingPixels: 32));

        result.Image.GetNormalized(center).Should().BeLessThan(0.7f);
        result.Provenance![center].Should().Be(RetouchProvenance.LowConfidenceHealing);
    }

    [Fact]
    public void HealChannel_HighConfidencePriorRepairRemainsEligibleGuideData()
    {
        const int size = 25;
        var target = Gradient(size, 0.20f, 0.42f);
        var guide = Gradient(size, 0.10f, 0.51f);
        var center = (size / 2 * size) + size / 2;
        target.SetNormalized(center, 1.0f);
        var result = ChannelHealer.HealChannel(
            target,
            new HealingGuide(guide, new RetouchProvenanceMap(size, size, RetouchProvenance.HighConfidenceHealing)),
            new HealingGuide(Gradient(size, 0.15f, 0.35f), new RetouchProvenanceMap(size, size, RetouchProvenance.CloneStamp)),
            SingleMask(size, size / 2, size / 2),
            new RetouchProvenanceMap(size, size),
            new HealOptions(ContextRadius: 12, MinTrainingPixels: 32));

        result.Provenance![center].Should().Be(RetouchProvenance.HighConfidenceHealing);
        result.Image.GetNormalized(center).Should().BeLessThan(0.7f);
    }

    [Fact]
    public void HealChannel_LargeUnresolvedInteriorDoesNotCompleteSceneFromGuides()
    {
        const int size = 31;
        var target = ImageBuffer.Filled(size, size, 0.2f);
        var guide1 = Gradient(size, 0.1f, 0.7f);
        var guide2 = Gradient(size, 0.2f, 0.5f);
        var mask = new byte[size * size];
        for (var y = 6; y < 25; y++)
        {
            for (var x = 6; x < 25; x++)
            {
                var index = (y * size) + x;
                target.SetNormalized(index, 0.91f);
                mask[index] = 1;
            }
        }

        var center = (size / 2 * size) + size / 2;
        var result = ChannelHealer.HealChannel(
            target,
            new HealingGuide(guide1, new RetouchProvenanceMap(size, size)),
            new HealingGuide(guide2, new RetouchProvenanceMap(size, size)),
            mask,
            new RetouchProvenanceMap(size, size),
            new HealOptions(ContextRadius: 4, MinTrainingPixels: 16, MaxComponentArea: 50));

        result.Image.GetNormalized(center).Should().BeApproximately(0.91f, 0.001f);
        result.Provenance![center].Should().Be(RetouchProvenance.LowConfidenceHealing);
    }

    [Fact]
    public void HealChannel_GuideDisagreementUsesConservativeLocalRepair()
    {
        const int size = 25;
        var target = Gradient(size, 0.20f, 0.45f);
        var guide1 = Gradient(size, 0.10f, 0.50f);
        var guide2 = VerticalGradient(size, 0.15f, 0.50f);
        var center = (size / 2 * size) + size / 2;
        target.SetNormalized(center, 1.0f);
        var result = Heal(target, guide1, guide2, SingleMask(size, size / 2, size / 2));

        result.IsLowConfidence.Should().BeTrue();
        result.Image.GetNormalized(center).Should().BeLessThan(0.75f);
        result.Provenance![center].Should().Be(RetouchProvenance.LowConfidenceHealing);
    }

    [Fact]
    public void HealChannel_RepairsScratchAcrossItsDirectionWithoutBlurringCrossedBoundary()
    {
        const int size = 31;
        var pristine = new ImageBuffer(size, size, new float[size * size]);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                pristine[x, y] = y < size / 2 ? 0.18f : 0.82f;
            }
        }

        var target = pristine.Clone();
        var mask = new byte[target.PixelCount];
        for (var y = 3; y < size - 3; y++)
        {
            var index = (y * size) + (size / 2);
            target.SetNormalized(index, 1.0f);
            mask[index] = 1;
        }

        var result = Heal(target, pristine.Clone(), pristine.Clone(), mask);

        result.GuidedSummary!.ScratchComponents.Should().Be(1);
        result.GuidedSummary.BoundarySegments.Should().BeGreaterThan(1);
        result.Image[size / 2, (size / 2) - 2].Should().BeLessThan(0.35f);
        result.Image[size / 2, (size / 2) + 2].Should().BeGreaterThan(0.65f);
    }

    [Fact]
    public void HealChannel_PreservesUInt16PrecisionRatherThanEightBitSteps()
    {
        const int size = 25;
        var target = new ImageBuffer(size, size, new ushort[size * size]);
        var guide1 = new ImageBuffer(size, size, new ushort[size * size]);
        var guide2 = new ImageBuffer(size, size, new ushort[size * size]);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var sample = (ushort)(17_113 + (x * 139) + (y * 73));
                target.UInt16Pixels[(y * size) + x] = sample;
                guide1.UInt16Pixels[(y * size) + x] = (ushort)(sample - 600);
                guide2.UInt16Pixels[(y * size) + x] = (ushort)(sample - 1_100);
            }
        }

        var center = (size / 2 * size) + size / 2;
        target.UInt16Pixels[center] = ushort.MaxValue;
        var result = Heal(target, guide1, guide2, SingleMask(size, size / 2, size / 2));

        result.Image.UInt16Pixels[center].Should().NotBe(ushort.MaxValue);
        (result.Image.UInt16Pixels[center] % 257).Should().NotBe(0);
    }

    [Fact]
    public void RetouchProvenanceMap_CropKeepsPixelTrustClassification()
    {
        var map = new RetouchProvenanceMap(4, 3);
        map[6] = RetouchProvenance.HighConfidenceHealing;
        map[10] = RetouchProvenance.CloneStamp;

        var cropped = map.Crop(1, 1, 2, 2);

        cropped[1].Should().Be(RetouchProvenance.HighConfidenceHealing);
        cropped[3].Should().Be(RetouchProvenance.CloneStamp);
    }

    private static HealResult Heal(ImageBuffer target, ImageBuffer guide1, ImageBuffer guide2, byte[] mask)
    {
        var debugOutputDirectory = Environment.GetEnvironmentVariable("PROKUDIN_VISUAL_VALIDATION_DIRECTORY");
        return ChannelHealer.HealChannel(
            target,
            new HealingGuide(guide1, new RetouchProvenanceMap(target.Width, target.Height)),
            new HealingGuide(guide2, new RetouchProvenanceMap(target.Width, target.Height)),
            mask,
            new RetouchProvenanceMap(target.Width, target.Height),
            new HealOptions(
                ContextRadius: 12,
                MinTrainingPixels: 32,
                DebugOutput: !string.IsNullOrWhiteSpace(debugOutputDirectory),
                DebugOutputDirectory: debugOutputDirectory));
    }

    private static byte[] SingleMask(int size, int x, int y)
    {
        var mask = new byte[size * size];
        mask[(y * size) + x] = 1;
        return mask;
    }

    private static ImageBuffer CreateGuide(int size, float offset) => Gradient(size, offset, 0.43f);

    private static ImageBuffer Gradient(int size, float offset, float scale)
    {
        var image = new ImageBuffer(size, size, new float[size * size]);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                image[x, y] = offset + (scale * x / (size - 1));
            }
        }

        return image;
    }

    private static ImageBuffer VerticalGradient(int size, float offset, float scale)
    {
        var image = new ImageBuffer(size, size, new float[size * size]);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                image[x, y] = offset + (scale * y / (size - 1));
            }
        }

        return image;
    }
}
