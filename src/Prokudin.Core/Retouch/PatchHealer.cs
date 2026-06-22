using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

internal readonly record struct PatchHealResult(float[] PatchValues, float Confidence, bool Succeeded, Point DonorCenter);

internal static class PatchHealer
{
    public static PatchHealResult HealComponent(
        ImageBuffer target,
        ImageBuffer? guide1,
        ImageBuffer? guide2,
        Mat componentMask,
        Mat globalDefectMask,
        HealOptions options,
        bool guided)
    {
        var width = target.Width;
        var height = target.Height;
        var patchValues = new float[width * height];
        target.CopyNormalizedTo(patchValues);

        var searchRadius = options.NormalizedSearchRadius;
        using var searchArea = HealingMaskUtils.BuildSearchArea(componentMask, searchRadius, options.NormalizedSafetyRadius);
        var donor = FindBestDonor(target, guide1, guide2, componentMask, globalDefectMask, searchArea, options, guided);
        if (!donor.Succeeded && searchRadius < 96)
        {
            using var expandedSearch = HealingMaskUtils.BuildSearchArea(componentMask, 96, options.NormalizedSafetyRadius);
            donor = FindBestDonor(target, guide1, guide2, componentMask, globalDefectMask, expandedSearch, options, guided);
        }

        if (!donor.Succeeded)
        {
            return new PatchHealResult(patchValues, 0.0f, false, default);
        }

        ApplyDonor(target, componentMask, patchValues, donor.Center, options.NormalizedPatchRadius);
        return new PatchHealResult(patchValues, donor.Confidence, true, donor.Center);
    }

    private readonly record struct DonorCandidate(Point Center, double Score, bool Succeeded, float Confidence);

    private static DonorCandidate FindBestDonor(
        ImageBuffer target,
        ImageBuffer? guide1,
        ImageBuffer? guide2,
        Mat componentMask,
        Mat globalDefectMask,
        Mat searchArea,
        HealOptions options,
        bool guided)
    {
        var width = target.Width;
        var height = target.Height;
        var patchRadius = options.NormalizedPatchRadius;
        var rect = HealingMaskUtils.BoundingRect(componentMask);
        var componentCenter = new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

        var maskGuide1 = guided && guide1 is not null ? ExtractPatchMeans(guide1, componentMask, patchRadius, width, height) : null;
        var maskGuide2 = guided && guide2 is not null ? ExtractPatchMeans(guide2, componentMask, patchRadius, width, height) : null;
        var maskBoundary = ExtractBoundaryMeans(target, componentMask, width, height);

        DonorCandidate? best = null;
        for (var y = patchRadius; y < height - patchRadius; y++)
        {
            for (var x = patchRadius; x < width - patchRadius; x++)
            {
                if (searchArea.At<byte>(y, x) == 0)
                {
                    continue;
                }

                if (!IsValidDonorCenter(x, y, patchRadius, width, height, globalDefectMask))
                {
                    continue;
                }

                var score = guided
                    ? ScoreGuidedDonor(
                        target,
                        guide1!,
                        guide2!,
                        x,
                        y,
                        patchRadius,
                        width,
                        height,
                        maskGuide1!,
                        maskGuide2!,
                        maskBoundary,
                        componentCenter,
                        options)
                    : ScoreSingleChannelDonor(target, x, y, patchRadius, width, height, maskBoundary, componentCenter, options);

                if (best is null || score < best.Value.Score)
                {
                    best = new DonorCandidate(new Point(x, y), score, true, 1.0f);
                }
            }
        }

        return best ?? new DonorCandidate(default, double.MaxValue, false, 0.0f);
    }

    private static double ScoreGuidedDonor(
        ImageBuffer target,
        ImageBuffer guide1,
        ImageBuffer guide2,
        int donorX,
        int donorY,
        int patchRadius,
        int width,
        int height,
        float[] maskGuide1,
        float[] maskGuide2,
        float[] maskBoundary,
        Point componentCenter,
        HealOptions options)
    {
        var donorGuide1 = ExtractPatchMeansAt(guide1, donorX, donorY, patchRadius, width, height);
        var donorGuide2 = ExtractPatchMeansAt(guide2, donorX, donorY, patchRadius, width, height);
        var donorBoundary = ExtractBoundaryMeansAt(target, donorX, donorY, patchRadius, width, height);

        var guideDiff = MeanAbs(maskGuide1, donorGuide1) + MeanAbs(maskGuide2, donorGuide2);
        var gradientDiff = GradientDifference(guide1, donorX, donorY, patchRadius, width, height, maskGuide1, donorGuide1) +
                           GradientDifference(guide2, donorX, donorY, patchRadius, width, height, maskGuide2, donorGuide2);
        var boundaryDiff = MeanAbs(maskBoundary, donorBoundary);
        var distance = Math.Sqrt(
            Math.Pow(donorX - componentCenter.X, 2) +
            Math.Pow(donorY - componentCenter.Y, 2));

        return (options.WGuide * guideDiff) +
               (options.WGradient * gradientDiff) +
               (options.WBoundary * boundaryDiff) +
               (options.WDistance * distance);
    }

    private static double ScoreSingleChannelDonor(
        ImageBuffer target,
        int donorX,
        int donorY,
        int patchRadius,
        int width,
        int height,
        float[] maskBoundary,
        Point componentCenter,
        HealOptions options)
    {
        var donorBoundary = ExtractBoundaryMeansAt(target, donorX, donorY, patchRadius, width, height);
        var boundaryDiff = MeanAbs(maskBoundary, donorBoundary);
        var distance = Math.Sqrt(
            Math.Pow(donorX - componentCenter.X, 2) +
            Math.Pow(donorY - componentCenter.Y, 2));

        return boundaryDiff + (options.WDistance * distance);
    }

    private static void ApplyDonor(
        ImageBuffer target,
        Mat componentMask,
        float[] patchValues,
        Point donorCenter,
        int patchRadius)
    {
        var width = target.Width;
        var height = target.Height;
        var rect = HealingMaskUtils.BoundingRect(componentMask);
        var offset = ComputeBrightnessOffset(target, componentMask, donorCenter, patchRadius, width, height);

        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                if (componentMask.At<byte>(y, x) == 0)
                {
                    continue;
                }

                var donorX = donorCenter.X + x - (rect.X + rect.Width / 2);
                var donorY = donorCenter.Y + y - (rect.Y + rect.Height / 2);
                if (donorX < 0 || donorY < 0 || donorX >= width || donorY >= height)
                {
                    continue;
                }

                var index = (y * width) + x;
                var donorIndex = (donorY * width) + donorX;
                patchValues[index] = Math.Clamp(target.GetNormalized(donorIndex) + offset, 0.0f, 1.0f);
            }
        }
    }

    private static float ComputeBrightnessOffset(
        ImageBuffer target,
        Mat componentMask,
        Point donorCenter,
        int patchRadius,
        int width,
        int height)
    {
        using var emptyDefects = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        using var ring = HealingMaskUtils.BuildRingMask(componentMask, patchRadius + 2, emptyDefects);
        var targetRing = new List<float>();
        var donorRing = new List<float>();
        var rect = HealingMaskUtils.BoundingRect(componentMask);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (ring.At<byte>(y, x) == 0)
                {
                    continue;
                }

                var donorX = donorCenter.X + x - (rect.X + rect.Width / 2);
                var donorY = donorCenter.Y + y - (rect.Y + rect.Height / 2);
                if (donorX < 0 || donorY < 0 || donorX >= width || donorY >= height)
                {
                    continue;
                }

                targetRing.Add(target.GetNormalized((y * width) + x));
                donorRing.Add(target.GetNormalized((donorY * width) + donorX));
            }
        }

        if (targetRing.Count == 0)
        {
            return 0.0f;
        }

        return Median(targetRing) - Median(donorRing);
    }

    private static bool IsValidDonorCenter(int x, int y, int patchRadius, int width, int height, Mat globalDefectMask)
    {
        for (var dy = -patchRadius; dy <= patchRadius; dy++)
        {
            for (var dx = -patchRadius; dx <= patchRadius; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= width || py >= height)
                {
                    return false;
                }

                if (globalDefectMask.At<byte>(py, px) > 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static float[] ExtractPatchMeans(ImageBuffer image, Mat mask, int patchRadius, int width, int height)
    {
        var rect = HealingMaskUtils.BoundingRect(mask);
        var center = new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        return ExtractPatchMeansAt(image, center.X, center.Y, patchRadius, width, height);
    }

    private static float[] ExtractPatchMeansAt(ImageBuffer image, int centerX, int centerY, int patchRadius, int width, int height)
    {
        var size = (patchRadius * 2) + 1;
        var values = new float[size * size];
        var index = 0;
        for (var dy = -patchRadius; dy <= patchRadius; dy++)
        {
            for (var dx = -patchRadius; dx <= patchRadius; dx++)
            {
                var x = Math.Clamp(centerX + dx, 0, width - 1);
                var y = Math.Clamp(centerY + dy, 0, height - 1);
                values[index++] = image.GetNormalized((y * width) + x);
            }
        }

        return values;
    }

    private static float[] ExtractBoundaryMeans(ImageBuffer image, Mat componentMask, int width, int height)
    {
        var rect = HealingMaskUtils.BoundingRect(componentMask);
        return ExtractBoundaryMeansAt(image, rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2), 3, width, height);
    }

    private static float[] ExtractBoundaryMeansAt(ImageBuffer image, int centerX, int centerY, int patchRadius, int width, int height)
    {
        var values = new List<float>();
        for (var dy = -patchRadius; dy <= patchRadius; dy++)
        {
            for (var dx = -patchRadius; dx <= patchRadius; dx++)
            {
                if (Math.Abs(dx) != patchRadius && Math.Abs(dy) != patchRadius)
                {
                    continue;
                }

                var x = Math.Clamp(centerX + dx, 0, width - 1);
                var y = Math.Clamp(centerY + dy, 0, height - 1);
                values.Add(image.GetNormalized((y * width) + x));
            }
        }

        return values.ToArray();
    }

    private static float GradientDifference(
        ImageBuffer image,
        int donorX,
        int donorY,
        int patchRadius,
        int width,
        int height,
        float[] maskPatch,
        float[] donorPatch)
    {
        var size = (patchRadius * 2) + 1;
        var gradientMask = new float[maskPatch.Length];
        var gradientDonor = new float[donorPatch.Length];
        var index = 0;
        for (var dy = -patchRadius; dy <= patchRadius; dy++)
        {
            for (var dx = -patchRadius; dx <= patchRadius; dx++)
            {
                var x = Math.Clamp(donorX + dx, 0, width - 1);
                var y = Math.Clamp(donorY + dy, 0, height - 1);
                var left = image.GetNormalized((y * width) + Math.Max(0, x - 1));
                var right = image.GetNormalized((y * width) + Math.Min(width - 1, x + 1));
                var up = image.GetNormalized((Math.Max(0, y - 1) * width) + x);
                var down = image.GetNormalized((Math.Min(height - 1, y + 1) * width) + x);
                gradientDonor[index] = Math.Abs(right - left) + Math.Abs(down - up);
                gradientMask[index] = maskPatch[index];
                index++;
            }
        }

        return MeanAbs(gradientMask, gradientDonor);
    }

    private static float MeanAbs(float[] left, float[] right)
    {
        var count = Math.Min(left.Length, right.Length);
        if (count == 0)
        {
            return 0.0f;
        }

        var sum = 0.0f;
        for (var i = 0; i < count; i++)
        {
            sum += Math.Abs(left[i] - right[i]);
        }

        return sum / count;
    }

    private static float Median(IReadOnlyList<float> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2.0f
            : sorted[middle];
    }
}
