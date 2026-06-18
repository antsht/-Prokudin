using System.Runtime.InteropServices;
using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

public static class ChannelRetoucher
{
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

    private static Mat FilterSmallDefects(Mat rawMask, int width, int height)
    {
        var filtered = new Mat(rawMask.Rows, rawMask.Cols, MatType.CV_8UC1, Scalar.Black);
        Cv2.FindContours(rawMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var maxArea = Math.Clamp((width * height) / 350, 4, 300);
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var boundingArea = rect.Width * rect.Height;
            if (boundingArea <= 0 || boundingArea > maxArea)
            {
                continue;
            }

            if (Math.Max(rect.Width, rect.Height) > 48)
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
}
