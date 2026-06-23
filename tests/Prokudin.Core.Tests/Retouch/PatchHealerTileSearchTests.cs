using FluentAssertions;
using OpenCvSharp;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class PatchHealerTileSearchTests
{
    [Fact]
    public void CoarseToFine_FindsSameDonorAsBruteForce_OnSyntheticScene()
    {
        const int size = 64;
        var target = CreateTextured(size, seed: 1);
        var guide1 = CreateTextured(size, seed: 2);
        var guide2 = CreateTextured(size, seed: 3);
        var options = new HealOptions(PatchRadius: 3, SearchRadius: 24, QualityMode: AutoCleanQualityMode.Quality);

        using var component = new Mat(size, size, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(component, new Point(30, 30), 4, Scalar.White, -1);
        using var globalMask = component.Clone();

        var brute = PatchHealer.HealComponent(
            target,
            guide1,
            guide2,
            component,
            globalMask,
            options,
            guided: true,
            patchValues: new float[size * size],
            useCoarseToFine: false);
        var fast = PatchHealer.HealComponent(
            target,
            guide1,
            guide2,
            component,
            globalMask,
            options,
            guided: true,
            patchValues: new float[size * size],
            useCoarseToFine: true);

        brute.Succeeded.Should().BeTrue();
        fast.Succeeded.Should().BeTrue();
        fast.DonorCenter.Should().Be(brute.DonorCenter);
    }

    [Fact]
    public void CoarseToFine_FindsDonor_OnLargeSearchArea()
    {
        const int size = 64;
        var target = CreateTextured(size, seed: 4);
        var guide1 = CreateTextured(size, seed: 5);
        var guide2 = CreateTextured(size, seed: 6);
        var options = new HealOptions(PatchRadius: 3, SearchRadius: 24);

        using var component = new Mat(size, size, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(component, new Point(30, 30), 4, Scalar.White, -1);
        using var globalMask = component.Clone();

        var fast = PatchHealer.HealComponent(
            target,
            guide1,
            guide2,
            component,
            globalMask,
            options,
            guided: true,
            patchValues: new float[size * size],
            useCoarseToFine: true);

        fast.Succeeded.Should().BeTrue();
    }

    private static ImageBuffer CreateTextured(int size, int seed)
    {
        var pixels = new float[size * size];
        var random = new Random(seed);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (float)random.NextDouble();
        }

        return ImageBuffer.FromNormalized(size, size, pixels, PixelFormat.Float32);
    }
}
