using System.Reflection;
using FluentAssertions;
using OpenCvSharp;
using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Alignment;

public sealed class ChannelAlignerTests
{
    [Fact]
    public void AlignChannel_AlignsShiftedSyntheticChannel()
    {
        var reference = SyntheticFeatureChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 7, dy: -5).Image;

        var result = ChannelAligner.AlignChannel(reference, moving, new AlignOptions(Detector: "sift", MaxFineIterations: 3));

        result.Image.Width.Should().Be(reference.Width);
        result.Image.Height.Should().Be(reference.Height);
        result.Mask.Count(value => value > 0).Should().BeGreaterThan(reference.Width * reference.Height / 2);
        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeLessThan(0.08f);
        result.SubpixelShifts.Should().NotBeEmpty();
    }

    [Fact]
    public void AlignChannel_AlignsUInt16ShiftedChannel()
    {
        var reference = SyntheticFeatureChannel().WithFormat(PixelFormat.UInt16);
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 7, dy: -5).Image;

        var result = ChannelAligner.AlignChannel(reference, moving, new AlignOptions(Detector: "sift", MaxFineIterations: 3));

        result.Image.Format.Should().Be(PixelFormat.UInt16);
        result.Image.Width.Should().Be(reference.Width);
        result.Image.Height.Should().Be(reference.Height);
        result.Mask.Count(value => value > 0).Should().BeGreaterThan(reference.Width * reference.Height / 2);
        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeLessThan(0.08f);
    }

    [Fact]
    public void AlignChannel_UsesIdentityWhenNoFeaturesExist()
    {
        var reference = ImageBuffer.Filled(64, 64, 0.25f);
        var moving = ImageBuffer.Filled(64, 64, 0.50f);

        var result = ChannelAligner.AlignChannel(reference, moving, new AlignOptions(Detector: "orb", MaxFineIterations: 1));

        result.TransformKind.Should().Be("identity");
        result.InlierCount.Should().Be(0);
        result.Mask.Should().OnlyContain(value => value == 1);
        Mean(result.Image).Should().BeApproximately(0.50f, 0.001f);
    }

    [Fact]
    public void AlignChannel_UsesIdentityForFeaturelessSiftChannel()
    {
        var reference = ImageBuffer.Filled(8, 8, 0.25f);
        var moving = ImageBuffer.Filled(8, 8, 0.50f);

        var result = ChannelAligner.AlignChannel(reference, moving, new AlignOptions(Detector: "sift", MaxFineIterations: 3));

        result.TransformKind.Should().Be("identity");
        result.InlierCount.Should().Be(0);
        result.Mask.Should().OnlyContain(value => value == 1);
        Mean(result.Image).Should().BeApproximately(0.50f, 0.001f);
    }

    [Fact]
    public void AlignChannel_AlignsLargeArchivalShift_WhenMaxTranslationAllows()
    {
        var reference = SyntheticFeatureChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 18, dy: -78).Image;

        var result = ChannelAligner.AlignChannel(
            reference,
            moving,
            new AlignOptions(Detector: "sift", MaxFineIterations: 3, MaxTranslation: 128));

        result.Image.Width.Should().Be(reference.Width);
        result.Image.Height.Should().Be(reference.Height);
        result.Mask.Count(value => value > 0).Should().BeGreaterThan((reference.Width - 18) * (reference.Height - 78) * 9 / 10);
        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeLessThan(0.08f);
        result.SubpixelShifts.Should().NotBeEmpty();
    }

    [Fact]
    public void AlignChannel_UsesDownsampledCoarseSearch_WhenMaxSideIsSmall()
    {
        var reference = SyntheticFeatureChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 7, dy: -5).Image;

        var result = ChannelAligner.AlignChannel(
            reference,
            moving,
            new AlignOptions(Detector: "sift", MaxFineIterations: 3, MaxTranslation: 32, CoarseAlignmentMaxSide: 64));

        result.Image.Width.Should().Be(reference.Width);
        result.Image.Height.Should().Be(reference.Height);
        result.Mask.Count(value => value > 0).Should().BeGreaterThan(reference.Width * reference.Height / 2);
        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeLessThan(0.08f);
    }

    [Fact]
    public void AlignChannel_RejectsLargeArchivalShift_WhenMaxTranslationTooSmall()
    {
        var reference = SyntheticFeatureChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 18, dy: -78).Image;

        var result = ChannelAligner.AlignChannel(
            reference,
            moving,
            new AlignOptions(Detector: "sift", MaxFineIterations: 3, MaxTranslation: 48));

        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeGreaterThan(0.08f);
    }

    [Theory]
    [InlineData(160, 144, 96)]
    [InlineData(3228, 3741, 129)]
    public void ResolveMaxTranslation_UsesDefaultOrAutoScale(int width, int height, int expected)
    {
        new AlignOptions(MaxTranslation: 128).ResolveMaxTranslation(width, height).Should().Be(128);
        new AlignOptions(MaxTranslation: 0).ResolveMaxTranslation(width, height).Should().Be(expected);
    }

    [Fact]
    public void AlignChannel_DoesNotApplyShiftsBeyondMaxTranslation()
    {
        var reference = SyntheticFeatureChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 18, dy: 0).Image;

        var result = ChannelAligner.AlignChannel(reference, moving, new AlignOptions(Detector: "sift", MaxFineIterations: 3, MaxTranslation: 2));

        result.SubpixelShifts.Should().OnlyContain(shift => Math.Abs(shift.Dx) <= 2 && Math.Abs(shift.Dy) <= 2);
        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeGreaterThan(0.08f);
    }

    [Fact]
    public void EstimateTransform_UsesAffineBranch_WhenHomographyHasTooFewInliers()
    {
        var source = new[]
        {
            new Point2f(0, 0),
            new Point2f(10, 0),
            new Point2f(20, 0),
            new Point2f(0, 10),
            new Point2f(10, 10),
            new Point2f(20, 10),
            new Point2f(5, 20),
            new Point2f(18, 22),
        };
        var destination = source.Select(point => new Point2f(point.X + 3, point.Y - 2)).ToArray();
        var method = typeof(ChannelAligner).GetMethod("EstimateTransform", BindingFlags.NonPublic | BindingFlags.Static);
        var arguments = new object?[] { source, destination, 32, null, 0 };

        method.Should().NotBeNull();
        using var matrix = (Mat)method!.Invoke(null, arguments)!;

        arguments[3].Should().Be("affine");
        arguments[4].Should().Be(source.Length);
        matrix.Rows.Should().Be(3);
        matrix.Cols.Should().Be(3);
        matrix.At<double>(0, 2).Should().BeApproximately(3.0, 1e-6);
        matrix.At<double>(1, 2).Should().BeApproximately(-2.0, 1e-6);
    }

    private static ImageBuffer SyntheticFeatureChannel()
    {
        var image = ImageBuffer.Filled(160, 144, 0.0f);
        DrawRect(image, 14, 20, 52, 70, 0.25f);
        DrawRect(image, 74, 18, 130, 44, 0.75f);
        DrawRect(image, 84, 78, 142, 118, 0.45f);
        DrawCircle(image, 42, 104, 16, 0.90f);
        DrawCircle(image, 112, 76, 11, 0.65f);

        for (var i = 0; i < 40; i++)
        {
            var x = 8 + ((i * 37) % 144);
            var y = 8 + ((i * 53) % 128);
            image[x, y] = 1.0f;
            image[Math.Min(image.Width - 1, x + 1), y] = 0.8f;
        }

        return image;
    }

    private static void DrawRect(ImageBuffer image, int x0, int y0, int x1, int y1, float value)
    {
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                image[x, y] = value;
            }
        }
    }

    private static void DrawCircle(ImageBuffer image, int centerX, int centerY, int radius, float value)
    {
        var radiusSquared = radius * radius;
        for (var y = centerY - radius; y <= centerY + radius; y++)
        {
            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                if (x >= 0 && y >= 0 && x < image.Width && y < image.Height)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    if ((dx * dx) + (dy * dy) <= radiusSquared)
                    {
                        image[x, y] = value;
                    }
                }
            }
        }
    }

    private static float MeanAbsoluteDifference(ImageBuffer reference, ImageBuffer aligned, byte[] mask)
    {
        var sum = 0.0f;
        var count = 0;
        for (var i = 0; i < reference.PixelCount; i++)
        {
            if (mask[i] == 0)
            {
                continue;
            }

            sum += Math.Abs(reference.GetNormalized(i) - aligned.GetNormalized(i));
            count++;
        }

        return sum / Math.Max(1, count);
    }

    private static float Mean(ImageBuffer image)
    {
        var sum = 0.0f;
        for (var i = 0; i < image.PixelCount; i++)
        {
            sum += image.GetNormalized(i);
        }

        return sum / image.PixelCount;
    }
}
