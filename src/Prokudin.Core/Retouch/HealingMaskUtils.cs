using OpenCvSharp;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Retouch;

internal static class HealingMaskUtils
{
    public static IReadOnlyList<Mat> FindComponents(byte[] mask, int width, int height)
    {
        using var maskMat = MaskToMat(mask, width, height);
        using var labels = new Mat();
        var componentCount = Cv2.ConnectedComponents(maskMat, labels, PixelConnectivity.Connectivity8, MatType.CV_32S);
        var components = new List<Mat>(Math.Max(0, componentCount - 1));
        for (var label = 1; label < componentCount; label++)
        {
            var component = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            using var labelMask = new Mat();
            Cv2.InRange(labels, new Scalar(label), new Scalar(label), labelMask);
            labelMask.ConvertTo(component, MatType.CV_8UC1, 255.0);
            components.Add(component);
        }

        return components;
    }

    public static Mat BuildRingMask(Mat componentMask, int contextRadius, Mat globalDefectMask)
    {
        using var dilated = Dilate(componentMask, contextRadius);
        using var ring = new Mat();
        Cv2.Subtract(dilated, componentMask, ring);
        using var invertedDefects = new Mat();
        Cv2.BitwiseNot(globalDefectMask, invertedDefects);
        var validRing = new Mat();
        Cv2.BitwiseAnd(ring, invertedDefects, validRing);
        return validRing;
    }

    public static Mat BuildSearchArea(Mat componentMask, int searchRadius, int safetyRadius)
    {
        using var outer = Dilate(componentMask, searchRadius);
        using var inner = Dilate(componentMask, safetyRadius);
        var search = new Mat();
        Cv2.Subtract(outer, inner, search);
        return search;
    }

    public static Mat BuildSoftMask(Mat componentMask, float featherSigma)
    {
        using var normalized = new Mat();
        componentMask.ConvertTo(normalized, MatType.CV_32FC1, 1.0 / 255.0);
        var soft = new Mat();
        var sigma = Math.Max(0.5, featherSigma);
        Cv2.GaussianBlur(normalized, soft, new Size(0, 0), sigmaX: sigma, sigmaY: sigma);
        Cv2.MinMaxLoc(soft, out _, out var max, out _, out _);
        if (max > 1e-6)
        {
            soft /= max;
        }

        return soft;
    }

    public static Mat MaskToMat(byte[] mask, int width, int height)
    {
        var normalized = new byte[mask.Length];
        PixelParallel.For(0, mask.Length, i =>
        {
            normalized[i] = mask[i] > 0 ? (byte)255 : (byte)0;
        });

        var mat = new Mat(height, width, MatType.CV_8UC1);
        System.Runtime.InteropServices.Marshal.Copy(normalized, 0, mat.Data, normalized.Length);
        return mat;
    }

    public static int CountNonZero(Mat mask)
    {
        return Cv2.CountNonZero(mask);
    }

    public static Rect BoundingRect(Mat mask) => Cv2.BoundingRect(mask);

    public static Mat MorphologicalClose(Mat mask, int radius)
    {
        var size = Math.Max(1, radius * 2 + 1);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(size, size));
        var closed = new Mat();
        Cv2.MorphologyEx(mask, closed, MorphTypes.Close, kernel);
        return closed;
    }

    public static Mat Dilate(Mat mask, int radius)
    {
        var size = Math.Max(1, radius * 2 + 1);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(size, size));
        var dilated = new Mat();
        Cv2.Dilate(mask, dilated, kernel);
        return dilated;
    }
}
