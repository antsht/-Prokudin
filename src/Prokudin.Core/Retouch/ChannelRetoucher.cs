using System.Runtime.InteropServices;
using OpenCvSharp;
using Prokudin.Core.Imaging;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Retouch;

public static class ChannelRetoucher
{
    private const double PercentileLow = 1.0;
    private const double PercentileHigh = 99.0;

    public static ImageBuffer InpaintMask(ImageBuffer image, byte[] mask, int radius)
    {
        if (mask.Length != image.Width * image.Height)
        {
            throw new ArgumentException("Mask dimensions must match the image.", nameof(mask));
        }

        if (!mask.Any(value => value > 0))
        {
            return image.Clone();
        }

        var format = image.Format;
        using var source = ImageMatConverter.ToUInt8MatForInpaint(image);
        using var maskMat = MaskToMat(mask, image.Width, image.Height);
        using var cleaned = new Mat();
        Cv2.Inpaint(source, maskMat, cleaned, Math.Clamp(radius, 1, 24), InpaintMethod.Telea);
        return ImageMatConverter.FromMat(cleaned, format);
    }

    public static RetouchResult AutoClean(ImageBuffer image, AutoCleanSettings settings)
    {
        using var source = ToU8(image);
        using var median = new Mat();
        Cv2.MedianBlur(source, median, 5);

        using var residual = new Mat();
        Cv2.Absdiff(source, median, residual);

        using var rawMask = new Mat();
        var threshold = 96.0 - (settings.NormalizedSensitivity * 0.72);
        Cv2.Threshold(residual, rawMask, threshold, 255, ThresholdTypes.Binary);

        using var filteredMask = FilterSmallDefects(rawMask, image.Width, image.Height);
        var preparedMask = PrepareAutoCleanMask(MaskFromMat(filteredMask), image.Width, image.Height, settings);
        var mask = preparedMask.FinalMask;
        var cleaned = mask.Any(value => value > 0)
            ? InpaintMask(image, mask, settings.NormalizedInpaintRadius)
            : image.Clone();

        return new RetouchResult(cleaned, mask);
    }

    public static AutoCleanMaskResult DetectSingleChannelDefects(
        ImageBuffer target,
        ImageBuffer other1,
        ImageBuffer other2,
        AutoCleanSettings settings,
        IProgress<double>? progress = null)
    {
        ReportProgress(progress, 0);
        ValidateSameDimensions(target, other1, nameof(other1));
        ValidateSameDimensions(target, other2, nameof(other2));

        float[] normalizedTarget = [];
        float[] normalizedOther1 = [];
        float[] normalizedOther2 = [];
        PixelParallel.Invoke(
            () => normalizedTarget = RobustNormalize(CopyNormalized(target)),
            () => normalizedOther1 = RobustNormalize(CopyNormalized(other1)),
            () => normalizedOther2 = RobustNormalize(CopyNormalized(other2)));
        ReportProgress(progress, 30);

        LinearModel predictionModel = default;
        float[] targetHighPass = [];
        float[] other1HighPass = [];
        float[] other2HighPass = [];
        PixelParallel.Invoke(
            () => predictionModel = FitPredictionModel(normalizedTarget, normalizedOther1, normalizedOther2),
            () => targetHighPass = HighPassAbs(normalizedTarget, target.Width, target.Height, sigma: 2.0),
            () => other1HighPass = HighPassAbs(normalizedOther1, target.Width, target.Height, sigma: 2.0),
            () => other2HighPass = HighPassAbs(normalizedOther2, target.Width, target.Height, sigma: 2.0));
        ReportProgress(progress, 75);

        var sensitivity = settings.NormalizedSensitivity / 100.0;
        var residualThreshold = 0.22 - (sensitivity * 0.18);
        var highPassThreshold = 0.10 - (sensitivity * 0.085);
        var supportMultiplier = 1.90 - (sensitivity * 0.90);
        var supportOffset = 0.020f - (float)(sensitivity * 0.015);
        var rawMaskBytes = new byte[normalizedTarget.Length];
        if (!ImageComputeBackendFactory.CreateBest().TryDetectDefectMask(
                normalizedTarget,
                normalizedOther1,
                normalizedOther2,
                targetHighPass,
                other1HighPass,
                other2HighPass,
                predictionModel.A,
                predictionModel.B,
                predictionModel.C,
                (float)residualThreshold,
                (float)highPassThreshold,
                (float)supportMultiplier,
                supportOffset,
                rawMaskBytes))
        {
            BuildRawMask(
                normalizedTarget,
                normalizedOther1,
                normalizedOther2,
                targetHighPass,
                other1HighPass,
                other2HighPass,
                predictionModel,
                (float)residualThreshold,
                (float)highPassThreshold,
                (float)supportMultiplier,
                supportOffset,
                rawMaskBytes);
        }

        ReportProgress(progress, 96);
        using var rawMask = MaskToMat(rawMaskBytes, target.Width, target.Height);
        using var filteredMask = FilterSmallDefects(rawMask, target.Width, target.Height, sensitivity);
        var result = PrepareAutoCleanMask(MaskFromMat(filteredMask), target.Width, target.Height, settings);
        ReportProgress(progress, 100);
        return result;
    }

    public static AutoCleanMaskResult PrepareAutoCleanMask(
        byte[] rawAutoMask,
        int width,
        int height,
        AutoCleanSettings settings)
    {
        ValidateMaskDimensions(rawAutoMask, width, height);

        var raw = NormalizeMask(rawAutoMask);
        var mergeRadius = settings.AutoMergeNearbyDefects ? settings.NormalizedAutoMergeDistancePx : 0;
        var expandRadius = settings.NormalizedAutoExpandHealingAreaPx;
        AutoCleanMaskResult result;
        do
        {
            result = PrepareAutoCleanMask(raw, width, height, mergeRadius, expandRadius);
            if (!HasOversizedComponent(result.FinalMask, width, height, settings.NormalizedMaxAutoExpandedComponentArea) ||
                (mergeRadius == 0 && expandRadius == 0))
            {
                break;
            }

            if (expandRadius > 0)
            {
                expandRadius--;
            }
            else
            {
                mergeRadius--;
            }
        }
        while (true);

        if (settings.DebugOutput)
        {
            HealingDebugWriter.SaveAutoCleanMaskDebug(settings, result, width, height);
        }

        return result;
    }

    public static byte[] CreateBrushMask(int width, int height, IReadOnlyList<RetouchStroke> strokes)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        using var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        foreach (var stroke in strokes)
        {
            DrawStroke(mask, width, height, stroke);
        }

        return MaskFromMat(mask);
    }

    public static RetouchResult Stamp(ImageBuffer image, CloneStampStroke stroke)
    {
        if (stroke.DestinationStroke.Points.Count == 0)
        {
            return new RetouchResult(image.Clone(), new byte[image.Width * image.Height]);
        }

        var destinationMask = CreateBrushMask(image.Width, image.Height, [stroke.DestinationStroke]);
        if (!destinationMask.Any(value => value > 0))
        {
            return new RetouchResult(image.Clone(), destinationMask);
        }

        var sourceMask = stroke.SourceMaskStroke is { Points.Count: > 0 } sourceMaskStroke
            ? CreateBrushMask(image.Width, image.Height, [sourceMaskStroke])
            : null;
        var alpha = CreateFeatherAlpha(destinationMask, image.Width, image.Height, stroke.BlendWidth);
        var destinationAnchor = stroke.DestinationStroke.Points[0];
        var result = image.Clone();
        var appliedMask = new byte[image.Width * image.Height];

        PixelParallel.ForRows(image.Height, y =>
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = (y * image.Width) + x;
                if (destinationMask[index] == 0 || alpha[index] <= 0)
                {
                    continue;
                }

                var sourceX = (int)MathF.Round(stroke.SourceAnchor.X + x - destinationAnchor.X);
                var sourceY = (int)MathF.Round(stroke.SourceAnchor.Y + y - destinationAnchor.Y);
                if (sourceX < 0 || sourceY < 0 || sourceX >= image.Width || sourceY >= image.Height)
                {
                    continue;
                }

                var sourceIndex = (sourceY * image.Width) + sourceX;
                if (sourceMask is not null && sourceMask[sourceIndex] == 0)
                {
                    continue;
                }

                var amount = alpha[index];
                result.SetNormalized(
                    index,
                    Math.Clamp(
                        (image.GetNormalized(index) * (1.0f - amount)) + (image.GetNormalized(sourceIndex) * amount),
                        0.0f,
                        1.0f));
                appliedMask[index] = 1;
            }
        });

        return new RetouchResult(result, appliedMask);
    }

    private static float[] CopyNormalized(ImageBuffer image)
    {
        var pixels = new float[image.PixelCount];
        image.CopyNormalizedTo(pixels);
        return pixels;
    }

    private static void ValidateSameDimensions(ImageBuffer target, ImageBuffer other, string parameterName)
    {
        if (target.Width != other.Width || target.Height != other.Height)
        {
            throw new ArgumentException("All channels must have the same dimensions.", parameterName);
        }
    }

    private static float[] RobustNormalize(float[] source)
    {
        var low = Percentile(source, PercentileLow);
        var high = Percentile(source, PercentileHigh);
        var normalized = new float[source.Length];
        if (high <= low)
        {
            PixelParallel.For(0, source.Length, i =>
            {
                normalized[i] = Math.Clamp(source[i], 0.0f, 1.0f);
            });

            return normalized;
        }

        var scale = 1.0f / (high - low);
        PixelParallel.For(0, source.Length, i =>
        {
            normalized[i] = Math.Clamp((source[i] - low) * scale, 0.0f, 1.0f);
        });

        return normalized;
    }

    private static float Percentile(float[] source, double percentile)
    {
        var sorted = (float[])source.Clone();
        Array.Sort(sorted);
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var position = Math.Clamp(percentile, 0.0, 100.0) / 100.0 * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var amount = (float)(position - lower);
        return (sorted[lower] * (1.0f - amount)) + (sorted[upper] * amount);
    }

    private static LinearModel FitPredictionModel(float[] target, float[] other1, float[] other2)
    {
        var lowTarget = Percentile(target, 2.0);
        var highTarget = Percentile(target, 98.0);
        var lowOther1 = Percentile(other1, 2.0);
        var highOther1 = Percentile(other1, 98.0);
        var lowOther2 = Percentile(other2, 2.0);
        var highOther2 = Percentile(other2, 98.0);

        var coefficients = FitLinearModel(
            target,
            other1,
            other2,
            i => IsCentralValue(target[i], lowTarget, highTarget) &&
                 IsCentralValue(other1[i], lowOther1, highOther1) &&
                 IsCentralValue(other2[i], lowOther2, highOther2));

        if (coefficients.Count == 0)
        {
            coefficients = FitLinearModel(target, other1, other2, _ => true);
        }

        return coefficients;
    }

    private static void BuildRawMask(
        float[] target,
        float[] other1,
        float[] other2,
        float[] targetHighPass,
        float[] other1HighPass,
        float[] other2HighPass,
        LinearModel model,
        float residualThreshold,
        float highPassThreshold,
        float supportMultiplier,
        float supportOffset,
        byte[] rawMaskBytes)
    {
        PixelParallel.For(0, target.Length, i =>
        {
            var prediction = Math.Clamp(
                (float)((model.A * other1[i]) + (model.B * other2[i]) + model.C),
                0.0f,
                1.0f);
            var residual = Math.Abs(target[i] - prediction);
            var otherSupport = Math.Max(other1HighPass[i], other2HighPass[i]);
            if (residual > residualThreshold &&
                targetHighPass[i] > highPassThreshold &&
                targetHighPass[i] > (otherSupport * supportMultiplier) + supportOffset)
            {
                rawMaskBytes[i] = 1;
            }
        });
    }

    private static bool IsCentralValue(float value, float low, float high)
    {
        return high > low && value > low && value < high;
    }

    private static LinearModel FitLinearModel(
        float[] target,
        float[] other1,
        float[] other2,
        Func<int, bool> include)
    {
        var totals = new LinearModelAccumulator();
        var gate = new object();
        PixelParallel.For(
            0,
            target.Length,
            localInit: static () => new LinearModelAccumulator(),
            body: (i, local) =>
            {
                if (include(i))
                {
                    local.Add(target[i], other1[i], other2[i]);
                }

                return local;
            },
            localFinally: local =>
            {
                lock (gate)
                {
                    totals.Add(local);
                }
            });

        if (totals.Count == 0)
        {
            return new LinearModel(0.0, 0.0, 0.0, 0);
        }

        var matrix = new[,]
        {
            { totals.SumX1X1 + 1e-6, totals.SumX1X2, totals.SumX1 },
            { totals.SumX1X2, totals.SumX2X2 + 1e-6, totals.SumX2 },
            { totals.SumX1, totals.SumX2, totals.Count + 1e-6 },
        };
        var vector = new[] { totals.SumX1Y, totals.SumX2Y, totals.SumY };
        if (!Solve3x3(matrix, vector, out var solution))
        {
            return new LinearModel(0.0, 0.0, totals.SumY / totals.Count, totals.Count);
        }

        return new LinearModel(solution[0], solution[1], solution[2], totals.Count);
    }

    private static bool Solve3x3(double[,] matrix, double[] vector, out double[] solution)
    {
        var augmented = new double[3, 4];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                augmented[row, column] = matrix[row, column];
            }

            augmented[row, 3] = vector[row];
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var bestRow = pivot;
            var bestValue = Math.Abs(augmented[pivot, pivot]);
            for (var row = pivot + 1; row < 3; row++)
            {
                var value = Math.Abs(augmented[row, pivot]);
                if (value > bestValue)
                {
                    bestValue = value;
                    bestRow = row;
                }
            }

            if (bestValue < 1e-12)
            {
                solution = [];
                return false;
            }

            if (bestRow != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    (augmented[pivot, column], augmented[bestRow, column]) =
                        (augmented[bestRow, column], augmented[pivot, column]);
                }
            }

            var pivotValue = augmented[pivot, pivot];
            for (var column = pivot; column < 4; column++)
            {
                augmented[pivot, column] /= pivotValue;
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = augmented[row, pivot];
                for (var column = pivot; column < 4; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        solution = [augmented[0, 3], augmented[1, 3], augmented[2, 3]];
        return true;
    }

    private static float[] HighPassAbs(float[] source, int width, int height, double sigma)
    {
        using var sourceMat = FloatToMat(source, width, height);
        using var blur = new Mat();
        Cv2.GaussianBlur(sourceMat, blur, new Size(0, 0), sigma);
        var blurred = new float[source.Length];
        Marshal.Copy(blur.Data, blurred, 0, blurred.Length);

        var highPass = new float[source.Length];
        PixelParallel.For(0, source.Length, i =>
        {
            highPass[i] = Math.Abs(source[i] - blurred[i]);
        });

        return highPass;
    }

    private static Mat FilterSmallDefects(Mat rawMask, int width, int height)
    {
        return FilterSmallDefects(rawMask, width, height, sensitivity: 0.0);
    }

    private static Mat FilterSmallDefects(Mat rawMask, int width, int height, double sensitivity)
    {
        var filtered = new Mat(rawMask.Rows, rawMask.Cols, MatType.CV_8UC1, Scalar.Black);
        Cv2.FindContours(rawMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var normalizedSensitivity = Math.Clamp(sensitivity, 0.0, 1.0);
        var maxAreaScale = 350.0 - (normalizedSensitivity * 180.0);
        var maxAreaLimit = (int)Math.Round(300.0 + (normalizedSensitivity * 700.0));
        var maxArea = Math.Clamp((int)Math.Round((width * height) / maxAreaScale), 4, maxAreaLimit);
        var maxLongSide = (int)Math.Round(48.0 + (normalizedSensitivity * 96.0));
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var boundingArea = rect.Width * rect.Height;
            if (boundingArea <= 0 || boundingArea > maxArea)
            {
                continue;
            }

            if (Math.Max(rect.Width, rect.Height) > maxLongSide)
            {
                continue;
            }

            Cv2.DrawContours(filtered, [contour], -1, Scalar.White, thickness: -1);
        }

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.Dilate(filtered, filtered, kernel, iterations: 1);
        return filtered;
    }

    private static void DrawStroke(Mat mask, int width, int height, RetouchStroke stroke)
    {
        if (stroke.Points.Count == 0)
        {
            return;
        }

        var thickness = Math.Clamp(stroke.BrushSize, 1, 200);
        var previous = ClampPoint(stroke.Points[0], width, height);
        Cv2.Circle(mask, previous, Math.Max(1, thickness / 2), Scalar.White, thickness: -1);

        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var current = ClampPoint(stroke.Points[i], width, height);
            Cv2.Line(mask, previous, current, Scalar.White, thickness, LineTypes.AntiAlias);
            Cv2.Circle(mask, current, Math.Max(1, thickness / 2), Scalar.White, thickness: -1);
            previous = current;
        }
    }

    private static Point ClampPoint(RetouchPoint point, int width, int height)
    {
        return new Point(
            Math.Clamp((int)MathF.Round(point.X), 0, width - 1),
            Math.Clamp((int)MathF.Round(point.Y), 0, height - 1));
    }

    private static float[] CreateFeatherAlpha(byte[] mask, int width, int height, int blendWidth)
    {
        var normalizedBlendWidth = Math.Clamp(blendWidth, 1, 24);
        if (normalizedBlendWidth <= 1)
        {
            var solidAlpha = new float[mask.Length];
            PixelParallel.For(0, mask.Length, i =>
            {
                solidAlpha[i] = mask[i] > 0 ? 1.0f : 0.0f;
            });

            return solidAlpha;
        }

        using var maskMat = MaskToMat(mask, width, height);
        using var distance = new Mat();
        Cv2.DistanceTransform(maskMat, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        var values = new float[width * height];
        Marshal.Copy(distance.Data, values, 0, values.Length);
        PixelParallel.For(0, values.Length, i =>
        {
            values[i] = mask[i] > 0
                ? Math.Clamp(values[i] / normalizedBlendWidth, 0.0f, 1.0f)
                : 0.0f;
        });

        return values;
    }

    private static Mat ToU8(ImageBuffer image) => ImageMatConverter.ToUInt8MatForInpaint(image);

    private static AutoCleanMaskResult PrepareAutoCleanMask(
        byte[] raw,
        int width,
        int height,
        int mergeRadius,
        int expandRadius)
    {
        using var rawMat = MaskToMat(raw, width, height);
        using var mergedMat = mergeRadius > 0
            ? MergeNearbyDefects(rawMat, mergeRadius)
            : rawMat.Clone();
        using var expandedMat = expandRadius > 0
            ? HealingMaskUtils.Dilate(mergedMat, expandRadius)
            : mergedMat.Clone();

        var merged = MaskFromMat(mergedMat);
        var expanded = MaskFromMat(expandedMat);
        var final = (byte[])expanded.Clone();
        return new AutoCleanMaskResult(final, final.Count(value => value > 0))
        {
            RawMask = (byte[])raw.Clone(),
            MergedMask = merged,
            ExpandedMask = expanded,
            FinalMask = final,
        };
    }

    private static Mat MergeNearbyDefects(Mat rawMask, int radius)
    {
        var originalComponentCount = CountConnectedComponents(rawMask);
        var closed = HealingMaskUtils.MorphologicalClose(rawMask, radius);
        if (CountConnectedComponents(closed) < originalComponentCount)
        {
            return closed;
        }

        var dilated = HealingMaskUtils.Dilate(rawMask, radius);
        if (CountConnectedComponents(dilated) < originalComponentCount)
        {
            closed.Dispose();
            return dilated;
        }

        dilated.Dispose();
        return closed;
    }

    private static int CountConnectedComponents(Mat mask)
    {
        using var labels = new Mat();
        return Cv2.ConnectedComponents(mask, labels, PixelConnectivity.Connectivity8, MatType.CV_32S) - 1;
    }

    private static bool HasOversizedComponent(byte[] mask, int width, int height, int maxArea)
    {
        var components = HealingMaskUtils.FindComponents(mask, width, height);
        try
        {
            return components.Any(component => HealingMaskUtils.CountNonZero(component) > maxArea);
        }
        finally
        {
            foreach (var component in components)
            {
                component.Dispose();
            }
        }
    }

    private static void ValidateMaskDimensions(byte[] mask, int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (mask.Length != width * height)
        {
            throw new ArgumentException("Mask dimensions must match the image.", nameof(mask));
        }
    }

    private static byte[] NormalizeMask(byte[] mask)
    {
        var normalized = new byte[mask.Length];
        PixelParallel.For(0, mask.Length, i =>
        {
            normalized[i] = mask[i] > 0 ? (byte)1 : (byte)0;
        });

        return normalized;
    }

    private static void ReportProgress(IProgress<double>? progress, double value)
    {
        progress?.Report(Math.Clamp(value, 0.0, 100.0));
    }

    private static Mat FloatToMat(float[] pixels, int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_32FC1);
        Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
        return mat;
    }

    private static Mat MaskToMat(byte[] mask, int width, int height)
    {
        var normalized = new byte[mask.Length];
        PixelParallel.For(0, mask.Length, i =>
        {
            normalized[i] = mask[i] > 0 ? (byte)255 : (byte)0;
        });

        var mat = new Mat(height, width, MatType.CV_8UC1);
        Marshal.Copy(normalized, 0, mat.Data, normalized.Length);
        return mat;
    }

    private static byte[] MaskFromMat(Mat mat)
    {
        using var u8 = new Mat();
        mat.ConvertTo(u8, MatType.CV_8UC1);
        var values = new byte[u8.Rows * u8.Cols];
        Marshal.Copy(u8.Data, values, 0, values.Length);
        PixelParallel.For(0, values.Length, i =>
        {
            values[i] = values[i] > 0 ? (byte)1 : (byte)0;
        });

        return values;
    }

    private sealed class LinearModelAccumulator
    {
        public int Count { get; private set; }

        public double SumX1 { get; private set; }

        public double SumX2 { get; private set; }

        public double SumY { get; private set; }

        public double SumX1X1 { get; private set; }

        public double SumX1X2 { get; private set; }

        public double SumX2X2 { get; private set; }

        public double SumX1Y { get; private set; }

        public double SumX2Y { get; private set; }

        public void Add(float y, float x1, float x2)
        {
            Count++;
            SumX1 += x1;
            SumX2 += x2;
            SumY += y;
            SumX1X1 += x1 * x1;
            SumX1X2 += x1 * x2;
            SumX2X2 += x2 * x2;
            SumX1Y += x1 * y;
            SumX2Y += x2 * y;
        }

        public void Add(LinearModelAccumulator other)
        {
            Count += other.Count;
            SumX1 += other.SumX1;
            SumX2 += other.SumX2;
            SumY += other.SumY;
            SumX1X1 += other.SumX1X1;
            SumX1X2 += other.SumX1X2;
            SumX2X2 += other.SumX2X2;
            SumX1Y += other.SumX1Y;
            SumX2Y += other.SumX2Y;
        }
    }

    private readonly record struct LinearModel(double A, double B, double C, int Count);
}
