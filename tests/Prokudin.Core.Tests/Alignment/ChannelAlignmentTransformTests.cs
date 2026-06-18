using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Alignment;

public sealed class ChannelAlignmentTransformTests
{
    [Fact]
    public void ApplyTransform_ReusesAutoAlignGeometry()
    {
        var reference = SyntheticChannel();
        var moving = ChannelAligner.WarpTranslation(reference, reference.Width, reference.Height, dx: 5, dy: -3).Image;

        var aligned = ChannelAligner.AlignChannel(
            reference,
            moving,
            new AlignOptions(MaxTranslation: 12, MaxFineIterations: 1));

        aligned.Transform.Should().NotBeNull();
        var reapplied = ChannelAligner.ApplyTransform(moving, aligned.Transform!);

        MeanAbsoluteDifference(aligned.Image, reapplied.Image, aligned.Mask).Should().BeLessThan(0.001f);
        reapplied.Mask.Should().Equal(aligned.Mask);
    }

    private static ImageBuffer SyntheticChannel()
    {
        var image = ImageBuffer.Filled(64, 64, 0.0f);
        for (var y = 12; y < 48; y++)
        {
            for (var x = 10; x < 50; x++)
            {
                image[x, y] = 0.25f;
            }
        }

        for (var y = 24; y < 38; y++)
        {
            for (var x = 26; x < 44; x++)
            {
                image[x, y] = 0.85f;
            }
        }

        return image;
    }

    private static float MeanAbsoluteDifference(ImageBuffer expected, ImageBuffer actual, byte[] mask)
    {
        var sum = 0.0f;
        var count = 0;
        for (var i = 0; i < expected.Pixels.Length; i++)
        {
            if (mask[i] == 0)
            {
                continue;
            }

            sum += Math.Abs(expected.Pixels[i] - actual.Pixels[i]);
            count++;
        }

        return sum / Math.Max(1, count);
    }
}
