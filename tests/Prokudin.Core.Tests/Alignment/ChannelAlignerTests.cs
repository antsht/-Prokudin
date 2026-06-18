using FluentAssertions;
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
    public void AlignChannel_DoesNotApplyShiftsBeyondMaxTranslation()
    {
        var reference = SyntheticFeatureChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 18, dy: 0).Image;

        var result = ChannelAligner.AlignChannel(reference, moving, new AlignOptions(Detector: "sift", MaxFineIterations: 3, MaxTranslation: 2));

        result.SubpixelShifts.Should().OnlyContain(shift => Math.Abs(shift.Dx) <= 2 && Math.Abs(shift.Dy) <= 2);
        MeanAbsoluteDifference(reference, result.Image, result.Mask).Should().BeGreaterThan(0.08f);
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
        for (var i = 0; i < reference.Pixels.Length; i++)
        {
            if (mask[i] == 0)
            {
                continue;
            }

            sum += Math.Abs(reference.Pixels[i] - aligned.Pixels[i]);
            count++;
        }

        return sum / Math.Max(1, count);
    }

    private static float Mean(ImageBuffer image)
    {
        return image.Pixels.Average();
    }
}
