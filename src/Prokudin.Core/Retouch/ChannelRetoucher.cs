using System.Runtime.InteropServices;
using OpenCvSharp;
using Prokudin.Core.Imaging;

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

        using var source = ToU8(image);
        using var maskMat = MaskToMat(mask, image.Width, image.Height);
        using var cleaned = new Mat();
        Cv2.Inpaint(source, maskMat, cleaned, Math.Clamp(radius, 1, 24), InpaintMethod.Telea);
        return FromU8(cleaned);
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
        var mask = MaskFromMat(filteredMask);
        var cleaned = mask.Any(value => value > 0)
            ? InpaintMask(image, mask, settings.NormalizedInpaintRadius)
            : image.Clone();

        return new RetouchResult(cleaned, mask);
    }

    public static AutoCleanMaskResult DetectSingleChannelDefects(
        ImageBuffer target,
        ImageBuffer other1,
        ImageBuffer other2,
        AutoCleanSettings settings)
    {
        ValidateSameDimensions(target, other1, nameof(other1));
        ValidateSameDimensions(target, other2, nameof(other2));

        var normalizedTarget = RobustNormalize(target.Pixels);
        var normalizedOther1 = RobustNormalize(other1.Pixels);
        var normalizedOther2 = RobustNormalize(other2.Pixels);
        var prediction = PredictChannel(normalizedTarget, normalizedOther1, normalizedOther2);
        var targetHighPass = HighPassAbs(normalizedTarget, target.Width, target.Height, sigma: 2.0);
        var other1HighPass = HighPassAbs(normalizedOther1, target.Width, target.Height, sigma: 2.0);
        var other2HighPass = HighPassAbs(normalizedOther2, target.Width, target.Height, sigma: 2.0);

        using var rawMask = new Mat(target.Height, target.Width, MatType.CV_8UC1, Scalar.Black);
        var sensitivity = settings.NormalizedSensitivity / 100.0;
        var residualThreshold = 0.22 - (sensitivity * 0.18);
        var highPassThreshold = 0.10 - (sensitivity * 0.085);
        var supportMultiplier = 1.90 - (sensitivity * 0.90);
        var supportOffset = 0.020f - (float)(sensitivity * 0.015);

        for (var i = 0; i < normalizedTarget.Length; i++)
        {
            var residual = Math.Abs(normalizedTarget[i] - prediction[i]);
            var otherSupport = Math.Max(other1HighPass[i], other2HighPass[i]);
            if (residual > residualThreshold &&
                targetHighPass[i] > highPassThreshold &&
                targetHighPass[i] > (otherSupport * supportMultiplier) + supportOffset)
            {
                rawMask.Set(i / target.Width, i % target.Width, (byte)255);
            }
        }

        using var filteredMask = FilterSmallDefects(rawMask, target.Width, target.Height, sensitivity);
        var mask = MaskFromMat(filteredMask);
        return new AutoCleanMaskResult(mask, mask.Count(value => value > 0));
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

        for (var y = 0; y < image.Height; y++)
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
                result.Pixels[index] = Math.Clamp(
                    (image.Pixels[index] * (1.0f - amount)) + (image.Pixels[sourceIndex] * amount),
                    0.0f,
                    1.0f);
                appliedMask[index] = 1;
            }
        }

        return new RetouchResult(result, appliedMask);
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
            for (var i = 0; i < source.Length; i++)
            {
                normalized[i] = Math.Clamp(source[i], 0.0f, 1.0f);
            }

            return normalized;
        }

        var scale = 1.0f / (high - low);
        for (var i = 0; i < source.Length; i++)
        {
            normalized[i] = Math.Clamp((source[i] - low) * scale, 0.0f, 1.0f);
        }

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

    private static float[] PredictChannel(float[] target, float[] other1, float[] other2)
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

        var prediction = new float[target.Length];
        for (var i = 0; i < prediction.Length; i++)
        {
            prediction[i] = Math.Clamp(
                (float)((coefficients.A * other1[i]) + (coefficients.B * other2[i]) + coefficients.C),
                0.0f,
                1.0f);
        }

        return prediction;
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
        var count = 0;
        var sumX1 = 0.0;
        var sumX2 = 0.0;
        var sumY = 0.0;
        var sumX1X1 = 0.0;
        var sumX1X2 = 0.0;
        var sumX2X2 = 0.0;
        var sumX1Y = 0.0;
        var sumX2Y = 0.0;

        for (var i = 0; i < target.Length; i++)
        {
            if (!include(i))
            {
                continue;
            }

            var x1 = other1[i];
            var x2 = other2[i];
            var y = target[i];
            count++;
            sumX1 += x1;
            sumX2 += x2;
            sumY += y;
            sumX1X1 += x1 * x1;
            sumX1X2 += x1 * x2;
            sumX2X2 += x2 * x2;
            sumX1Y += x1 * y;
            sumX2Y += x2 * y;
        }

        if (count == 0)
        {
            return new LinearModel(0.0, 0.0, 0.0, 0);
        }

        var matrix = new[,]
        {
            { sumX1X1 + 1e-6, sumX1X2, sumX1 },
            { sumX1X2, sumX2X2 + 1e-6, sumX2 },
            { sumX1, sumX2, count + 1e-6 },
        };
        var vector = new[] { sumX1Y, sumX2Y, sumY };
        if (!Solve3x3(matrix, vector, out var solution))
        {
            return new LinearModel(0.0, 0.0, sumY / count, count);
        }

        return new LinearModel(solution[0], solution[1], solution[2], count);
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
        for (var i = 0; i < source.Length; i++)
        {
            highPass[i] = Math.Abs(source[i] - blurred[i]);
        }

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
            for (var i = 0; i < mask.Length; i++)
            {
                solidAlpha[i] = mask[i] > 0 ? 1.0f : 0.0f;
            }

            return solidAlpha;
        }

        using var maskMat = MaskToMat(mask, width, height);
        using var distance = new Mat();
        Cv2.DistanceTransform(maskMat, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        var values = new float[width * height];
        Marshal.Copy(distance.Data, values, 0, values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = mask[i] > 0
                ? Math.Clamp(values[i] / normalizedBlendWidth, 0.0f, 1.0f)
                : 0.0f;
        }

        return values;
    }

    private static Mat ToU8(ImageBuffer image)
    {
        var mat = new Mat(image.Height, image.Width, MatType.CV_32FC1);
        Marshal.Copy(image.Pixels, 0, mat.Data, image.Pixels.Length);
        var u8 = new Mat();
        mat.ConvertTo(u8, MatType.CV_8UC1, 255.0);
        mat.Dispose();
        return u8;
    }

    private static ImageBuffer FromU8(Mat mat)
    {
        using var floatMat = new Mat();
        mat.ConvertTo(floatMat, MatType.CV_32FC1, 1.0 / 255.0);
        var pixels = new float[floatMat.Rows * floatMat.Cols];
        Marshal.Copy(floatMat.Data, pixels, 0, pixels.Length);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Math.Clamp(pixels[i], 0.0f, 1.0f);
        }

        return new ImageBuffer(floatMat.Cols, floatMat.Rows, pixels);
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
        for (var i = 0; i < mask.Length; i++)
        {
            normalized[i] = mask[i] > 0 ? (byte)255 : (byte)0;
        }

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
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = values[i] > 0 ? (byte)1 : (byte)0;
        }

        return values;
    }

    private readonly record struct LinearModel(double A, double B, double C, int Count);
}
