using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Alignment;

public static class ChannelAligner
{
    private const int MinimumHomographyInliers = 15;
    private const int MinimumAffineInliers = 8;
    private const double LowInlierRatio = 0.3;

    public static AlignResult AlignChannel(ImageBuffer reference, ImageBuffer moving, AlignOptions? options = null)
    {
        options ??= new AlignOptions();
        var maxTranslation = options.ResolveMaxTranslation(reference.Width, reference.Height);

        using var refMat = ToMat(reference);
        using var movMat = moving.Width == reference.Width && moving.Height == reference.Height
            ? ToMat(moving)
            : Resize(ToMat(moving), reference.Width, reference.Height);

        return AlignChannel(
            refMat,
            movMat,
            moving.Width,
            moving.Height,
            moving.Format,
            options.Detector,
            Math.Max(0, options.MaxFineIterations),
            maxTranslation,
            allowOrbRetry: true);
    }

    public static (ImageBuffer Image, byte[] Mask) ApplyTransform(ImageBuffer moving, ChannelAlignmentTransform transform)
    {
        if (!transform.CanApplyTo(moving))
        {
            throw new ArgumentException("Transform source dimensions must match the image.", nameof(transform));
        }

        using var movingMat = moving.Width == transform.OutputWidth && moving.Height == transform.OutputHeight
            ? ToMat(moving)
            : Resize(ToMat(moving), transform.OutputWidth, transform.OutputHeight);
        using var matrix = MatrixFromTransform(transform);
        using var warped = new Mat();
        using var mask = new Mat();
        WarpChannel(movingMat, matrix, new Size(transform.OutputWidth, transform.OutputHeight), transform.TransformKind, warped, mask);

        var aligned = warped.Clone();
        var alignedMask = mask.Clone();
        foreach (var (dx, dy) in transform.Shifts)
        {
            var translated = ApplyTranslation(aligned, alignedMask, dx, dy);
            aligned.Dispose();
            alignedMask.Dispose();
            aligned = translated.Image;
            alignedMask = translated.Mask;
        }

        using (aligned)
        using (alignedMask)
        {
            return (FromMat(aligned, moving.Format), MaskFromMat(alignedMask));
        }
    }

    public static (ImageBuffer Image, byte[] Mask) WarpTranslation(ImageBuffer moving, int width, int height, int dx, int dy)
    {
        using var movingMat = ToMat(moving);
        using var mask = Ones(moving.Height, moving.Width);
        using var matrix = TranslationMatrix(dx, dy);
        using var warped = new Mat();
        using var warpedMask = new Mat();
        Cv2.WarpAffine(movingMat, warped, matrix, new Size(width, height), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        Cv2.WarpAffine(mask, warpedMask, matrix, new Size(width, height), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
        return (FromMat(warped, moving.Format), MaskFromMat(warpedMask));
    }

    public static void SaveAlignmentDebug(ImageBuffer reference, ImageBuffer alignedRed, ImageBuffer alignedBlue, string debugDir)
    {
        Directory.CreateDirectory(debugDir);

        using var referenceMat = ToMat(reference);
        using var redMat = ToMat(alignedRed);
        using var blueMat = ToMat(alignedBlue);
        using var er = NormalizedEdges(redMat);
        using var eg = NormalizedEdges(referenceMat);
        using var eb = NormalizedEdges(blueMat);
        using var overlay = new Mat(reference.Height, reference.Width, MatType.CV_8UC3, Scalar.Black);

        Cv2.Merge(new[] { eb, eg, er }, overlay);
        Cv2.ImWrite(Path.Combine(debugDir, "edge_overlay_rgb.png"), overlay);

        using var diff = new Mat();
        using var diffNorm = new Mat();
        Cv2.Absdiff(redMat, referenceMat, diff);
        Cv2.Normalize(diff, diffNorm, 0, 255, NormTypes.MinMax);
        using var diffU8 = new Mat();
        diffNorm.ConvertTo(diffU8, MatType.CV_8UC1);
        Cv2.ImWrite(Path.Combine(debugDir, "r_g_diff_heatmap.png"), diffU8);
    }

    private static AlignResult AlignChannel(
        Mat reference,
        Mat moving,
        int sourceWidth,
        int sourceHeight,
        PixelFormat outputFormat,
        string detector,
        int maxFineIterations,
        int maxTranslation,
        bool allowOrbRetry)
    {
        detector = NormalizeDetector(detector);
        var (src, dst, matchCount) = MatchFeatures(reference, moving, detector);

        using var warped = new Mat();
        using var mask = new Mat();
        string kind = "identity";
        int inliers = 0;

        using var matrix = src.Length >= 4
            ? EstimateTransform(src, dst, maxTranslation, out kind, out inliers)
            : HomogeneousTranslationMatrix(0, 0);

        if (src.Length >= 4)
        {
            WarpChannel(moving, matrix, reference.Size(), kind, warped, mask);
        }
        else
        {
            kind = "identity";
            inliers = 0;
            moving.CopyTo(warped);
            using var ones = Ones(reference.Rows, reference.Cols);
            ones.CopyTo(mask);
        }

        if (allowOrbRetry && detector == "sift" && matchCount > 0 && inliers / (double)matchCount < LowInlierRatio)
        {
            return AlignChannel(reference, moving, sourceWidth, sourceHeight, outputFormat, "orb", maxFineIterations, maxTranslation, allowOrbRetry: false);
        }

        var (fineImage, fineMask, shifts) = FineAlign(reference, warped, mask, maxFineIterations, maxTranslation);
        var transform = TransformFromMatrix(sourceWidth, sourceHeight, reference.Cols, reference.Rows, kind, matrix, shifts);
        using (fineImage)
        using (fineMask)
        {
            return new AlignResult(FromMat(fineImage, outputFormat), MaskFromMat(fineMask), kind, inliers, shifts, transform);
        }
    }

    private static (Point2f[] Src, Point2f[] Dst, int MatchCount) MatchFeatures(Mat reference, Mat moving, string detector)
    {
        using var refU8 = ToU8(reference);
        using var movU8 = ToU8(moving);
        using var feature = CreateDetector(detector);
        using var refDescriptors = new Mat();
        using var movDescriptors = new Mat();

        feature.DetectAndCompute(movU8, null, out KeyPoint[] movingKeypoints, movDescriptors);
        feature.DetectAndCompute(refU8, null, out KeyPoint[] referenceKeypoints, refDescriptors);

        if (movDescriptors.Empty() || refDescriptors.Empty() || movingKeypoints.Length < 4 || referenceKeypoints.Length < 4)
        {
            return ([], [], 0);
        }

        using var matcher = new BFMatcher(detector == "sift" ? NormTypes.L2 : NormTypes.Hamming);
        var pairs = matcher.KnnMatch(movDescriptors, refDescriptors, k: 2);
        var good = new List<DMatch>();
        foreach (var pair in pairs)
        {
            if (pair.Length >= 2 && pair[0].Distance < 0.75 * pair[1].Distance)
            {
                good.Add(pair[0]);
            }
        }

        if (good.Count < 4)
        {
            return ([], [], good.Count);
        }

        var src = new Point2f[good.Count];
        var dst = new Point2f[good.Count];
        for (var i = 0; i < good.Count; i++)
        {
            src[i] = movingKeypoints[good[i].QueryIdx].Pt;
            dst[i] = referenceKeypoints[good[i].TrainIdx].Pt;
        }

        return (src, dst, good.Count);
    }

    private static Feature2D CreateDetector(string detector)
    {
        return detector switch
        {
            "sift" => SIFT.Create(8000),
            "orb" => ORB.Create(nFeatures: 5000, scaleFactor: 1.2f, nLevels: 8),
            _ => throw new ArgumentException("Detector must be 'sift' or 'orb'.", nameof(detector)),
        };
    }

    private static Mat EstimateTransform(Point2f[] src, Point2f[] dst, int maxTranslation, out string kind, out int inliers)
    {
        using var homographyInliers = new Mat();
        var srcD = src.Select(point => new Point2d(point.X, point.Y)).ToArray();
        var dstD = dst.Select(point => new Point2d(point.X, point.Y)).ToArray();
        var homography = Cv2.FindHomography(srcD, dstD, HomographyMethods.Ransac, 3.0, homographyInliers);
        inliers = CountInliers(homographyInliers);
        if (homography is not null && !homography.Empty() && inliers >= MinimumHomographyInliers)
        {
            if (IsWithinTranslationLimit(homography, maxTranslation))
            {
                kind = "homography";
                return homography;
            }
        }

        homography?.Dispose();

        using var affineInliers = new Mat();
        using var srcInput = InputArray.Create(src);
        using var dstInput = InputArray.Create(dst);
        var affine = Cv2.EstimateAffinePartial2D(srcInput, dstInput, affineInliers, RobustEstimationAlgorithms.RANSAC, 3.0);
        inliers = CountInliers(affineInliers);
        if (affine is not null && !affine.Empty() && inliers >= MinimumAffineInliers)
        {
            using var affineHomogeneous = ToHomogeneous(affine);
            if (IsWithinTranslationLimit(affineHomogeneous, maxTranslation))
            {
                kind = "affine";
                return affineHomogeneous.Clone();
            }
        }

        affine?.Dispose();

        var (dx, dy) = MedianDelta(src, dst);
        kind = "translation";
        inliers = src.Length;
        return IsWithinTranslationLimit(dx, dy, maxTranslation)
            ? HomogeneousTranslationMatrix(dx, dy)
            : HomogeneousTranslationMatrix(0, 0);
    }

    private static void WarpChannel(Mat moving, Mat matrix, Size canvasSize, string kind, Mat warped, Mat mask)
    {
        using var sourceMask = Ones(moving.Rows, moving.Cols);
        if (kind == "homography")
        {
            Cv2.WarpPerspective(moving, warped, matrix, canvasSize, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
            Cv2.WarpPerspective(sourceMask, mask, matrix, canvasSize, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
            return;
        }

        using var affine = new Mat(matrix, new Rect(0, 0, 3, 2));
        Cv2.WarpAffine(moving, warped, affine, canvasSize, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        Cv2.WarpAffine(sourceMask, mask, affine, canvasSize, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
    }

    private static (Mat Image, Mat Mask, IReadOnlyList<(float Dx, float Dy)> Shifts) FineAlign(
        Mat reference,
        Mat moving,
        Mat mask,
        int maxIterations,
        int maxTranslation)
    {
        var aligned = moving.Clone();
        var alignedMask = mask.Clone();
        var shifts = new List<(float Dx, float Dy)>();

        for (var i = 0; i < maxIterations; i++)
        {
            using var overlap = OverlapMask(reference, alignedMask);
            var (dx, dy) = PhaseCorrelationShift(reference, aligned, overlap);
            if (!IsWithinTranslationLimit(dx, dy, maxTranslation))
            {
                break;
            }

            shifts.Add(((float)dx, (float)dy));
            if (Math.Abs(dx) + Math.Abs(dy) < 0.05)
            {
                break;
            }

            var translated = ApplyTranslation(aligned, alignedMask, dx, dy);
            aligned.Dispose();
            alignedMask.Dispose();
            aligned = translated.Image;
            alignedMask = translated.Mask;
        }

        var (eccImage, eccMask, eccShift) = EccRefinement(reference, aligned, alignedMask, Math.Max(1, maxIterations * 16));
        if (!IsWithinTranslationLimit(eccShift.Dx, eccShift.Dy, maxTranslation))
        {
            eccImage.Dispose();
            eccMask.Dispose();
            return (aligned, alignedMask, shifts);
        }

        aligned.Dispose();
        alignedMask.Dispose();
        if (Math.Abs(eccShift.Dx) + Math.Abs(eccShift.Dy) > 0.01)
        {
            shifts.Add(eccShift);
        }

        return (eccImage, eccMask, shifts);
    }

    private static (double Dx, double Dy) PhaseCorrelationShift(Mat reference, Mat moving, Mat? mask)
    {
        using var refEdges = EdgeMap(reference);
        using var movEdges = EdgeMap(moving);
        if (mask is not null)
        {
            using var maskFloat = new Mat();
            mask.ConvertTo(maskFloat, MatType.CV_32FC1);
            Cv2.Multiply(refEdges, maskFloat, refEdges);
            Cv2.Multiply(movEdges, maskFloat, movEdges);
        }

        if (Cv2.Norm(refEdges) < 1e-6 || Cv2.Norm(movEdges) < 1e-6)
        {
            return (0.0, 0.0);
        }

        using var window = new Mat();
        var shift = Cv2.PhaseCorrelate(movEdges, refEdges, window, out _);
        return double.IsFinite(shift.X) && double.IsFinite(shift.Y)
            ? (shift.X, shift.Y)
            : (0.0, 0.0);
    }

    private static (Mat Image, Mat Mask, (float Dx, float Dy) Shift) EccRefinement(Mat reference, Mat moving, Mat mask, int maxIterations)
    {
        var roi = CentralOverlapRoi(mask);
        if (roi.Width < 32 || roi.Height < 32)
        {
            roi = new Rect(0, 0, reference.Cols, reference.Rows);
        }

        using var refRoi = new Mat(reference, roi);
        using var movRoi = new Mat(moving, roi);
        using var warp = Identity(2, 3, MatType.CV_32FC1);
        if (StandardDeviation(refRoi) < 1e-6 || StandardDeviation(movRoi) < 1e-6)
        {
            return (moving.Clone(), mask.Clone(), (0.0f, 0.0f));
        }

        try
        {
            Cv2.FindTransformECC(
                refRoi,
                movRoi,
                warp,
                MotionTypes.Translation,
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.Count, maxIterations, 1e-5),
                null,
                1);
        }
        catch (OpenCVException)
        {
            return (moving.Clone(), mask.Clone(), (0.0f, 0.0f));
        }

        var dx = warp.At<float>(0, 2);
        var dy = warp.At<float>(1, 2);
        var translated = ApplyTranslation(moving, mask, dx, dy);
        return (translated.Image, translated.Mask, (dx, dy));
    }

    private static double StandardDeviation(Mat image)
    {
        Cv2.MeanStdDev(image, out _, out var stddev);
        return stddev.Val0;
    }

    private static (Mat Image, Mat Mask) ApplyTranslation(Mat image, Mat mask, double dx, double dy)
    {
        using var matrix = TranslationMatrix(dx, dy);
        var warped = new Mat();
        var warpedMask = new Mat();
        Cv2.WarpAffine(image, warped, matrix, image.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        Cv2.WarpAffine(mask, warpedMask, matrix, mask.Size(), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.Black);
        Cv2.Threshold(warpedMask, warpedMask, 0.5, 1.0, ThresholdTypes.Binary);
        return (warped, warpedMask);
    }

    private static Mat EdgeMap(Mat image)
    {
        using var u8 = ToU8(image);
        using var gx = new Mat();
        using var gy = new Mat();
        var magnitude = new Mat();
        Cv2.Sobel(u8, gx, MatType.CV_32FC1, 1, 0, ksize: 3);
        Cv2.Sobel(u8, gy, MatType.CV_32FC1, 0, 1, ksize: 3);
        Cv2.Magnitude(gx, gy, magnitude);
        return magnitude;
    }

    private static Mat NormalizedEdges(Mat image)
    {
        using var edges = EdgeMap(image);
        using var normalized = new Mat();
        Cv2.Normalize(edges, normalized, 0, 255, NormTypes.MinMax);
        using var u8 = new Mat();
        normalized.ConvertTo(u8, MatType.CV_8UC1);
        return u8.Clone();
    }

    private static bool IsWithinTranslationLimit(Mat matrix, int maxTranslation)
    {
        return IsWithinTranslationLimit(matrix.At<double>(0, 2), matrix.At<double>(1, 2), maxTranslation);
    }

    private static bool IsWithinTranslationLimit(double dx, double dy, int maxTranslation)
    {
        return Math.Abs(dx) <= maxTranslation && Math.Abs(dy) <= maxTranslation;
    }

    private static Mat OverlapMask(Mat reference, Mat mask)
    {
        using var refPositive = new Mat();
        var overlap = new Mat();
        Cv2.Compare(reference, Scalar.Black, refPositive, CmpType.GT);
        Cv2.BitwiseAnd(refPositive, mask, overlap);
        Cv2.Threshold(overlap, overlap, 0, 1, ThresholdTypes.Binary);
        return overlap;
    }

    private static Rect CentralOverlapRoi(Mat mask)
    {
        using var pointsMat = new Mat();
        Cv2.FindNonZero(mask, pointsMat);
        pointsMat.GetArray(out Point[] points);
        if (points.Length == 0)
        {
            return new Rect();
        }

        var xs = points.Select(point => point.X).Order().ToArray();
        var ys = points.Select(point => point.Y).Order().ToArray();
        var x0 = Percentile(xs, 0.10);
        var x1 = Percentile(xs, 0.90);
        var y0 = Percentile(ys, 0.10);
        var y1 = Percentile(ys, 0.90);
        return new Rect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    private static int Percentile(int[] sortedValues, double percentile)
    {
        var index = (int)Math.Clamp(Math.Floor((sortedValues.Length - 1) * percentile), 0, sortedValues.Length - 1);
        return sortedValues[index];
    }

    private static int CountInliers(Mat inlierMask)
    {
        return inlierMask.Empty() ? 0 : Cv2.CountNonZero(inlierMask);
    }

    private static (double Dx, double Dy) MedianDelta(Point2f[] src, Point2f[] dst)
    {
        var dx = new double[src.Length];
        var dy = new double[src.Length];
        for (var i = 0; i < src.Length; i++)
        {
            dx[i] = dst[i].X - src[i].X;
            dy[i] = dst[i].Y - src[i].Y;
        }

        Array.Sort(dx);
        Array.Sort(dy);
        return (Median(dx), Median(dy));
    }

    private static double Median(double[] sortedValues)
    {
        var middle = sortedValues.Length / 2;
        return sortedValues.Length % 2 == 0
            ? (sortedValues[middle - 1] + sortedValues[middle]) / 2.0
            : sortedValues[middle];
    }

    private static Mat ToHomogeneous(Mat affine)
    {
        var matrix = Identity(3, 3, MatType.CV_64FC1);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                matrix.Set<double>(y, x, affine.At<double>(y, x));
            }
        }

        return matrix;
    }

    private static Mat HomogeneousTranslationMatrix(double dx, double dy)
    {
        var matrix = Identity(3, 3, MatType.CV_64FC1);
        matrix.Set<double>(0, 2, dx);
        matrix.Set<double>(1, 2, dy);
        return matrix;
    }

    private static Mat TranslationMatrix(double dx, double dy)
    {
        var matrix = Identity(2, 3, MatType.CV_32FC1);
        matrix.Set<float>(0, 2, (float)dx);
        matrix.Set<float>(1, 2, (float)dy);
        return matrix;
    }

    private static Mat Ones(int rows, int cols)
    {
        return new Mat(rows, cols, MatType.CV_8UC1, Scalar.All(1));
    }

    private static Mat Identity(int rows, int cols, MatType type)
    {
        var matrix = new Mat(rows, cols, type, Scalar.All(0));
        for (var i = 0; i < Math.Min(rows, cols); i++)
        {
            if (type == MatType.CV_64FC1)
            {
                matrix.Set<double>(i, i, 1.0);
            }
            else
            {
                matrix.Set<float>(i, i, 1.0f);
            }
        }

        return matrix;
    }

    private static Mat Resize(Mat source, int width, int height)
    {
        var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, height), interpolation: InterpolationFlags.Linear);
        source.Dispose();
        return resized;
    }

    private static ChannelAlignmentTransform TransformFromMatrix(
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        string kind,
        Mat matrix,
        IReadOnlyList<(float Dx, float Dy)> shifts)
    {
        using var doubleMatrix = new Mat();
        matrix.ConvertTo(doubleMatrix, MatType.CV_64FC1);
        var values = new double[doubleMatrix.Rows * doubleMatrix.Cols];
        Marshal.Copy(doubleMatrix.Data, values, 0, values.Length);
        return new ChannelAlignmentTransform(
            sourceWidth,
            sourceHeight,
            outputWidth,
            outputHeight,
            kind,
            doubleMatrix.Rows,
            doubleMatrix.Cols,
            values,
            shifts.ToArray());
    }

    private static Mat MatrixFromTransform(ChannelAlignmentTransform transform)
    {
        if (transform.Matrix.Length != transform.MatrixRows * transform.MatrixColumns)
        {
            throw new ArgumentException("Transform matrix dimensions do not match its values.", nameof(transform));
        }

        var matrix = new Mat(transform.MatrixRows, transform.MatrixColumns, MatType.CV_64FC1);
        Marshal.Copy(transform.Matrix, 0, matrix.Data, transform.Matrix.Length);
        return matrix;
    }

    // Alignment assumes OpenCV mats in normalized float [0, 1] regardless of ImageBuffer storage.
    private static Mat ToMat(ImageBuffer image) => ImageMatConverter.ToNormalizedFloatMat(image);

    private static ImageBuffer FromMat(Mat mat, PixelFormat format)
    {
        var aligned = ImageMatConverter.FromMat(mat, PixelFormat.Float32);
        return format == PixelFormat.Float32 ? aligned : aligned.WithFormat(format);
    }

    private static Mat ToU8(Mat mat)
    {
        if (mat.Type() == MatType.CV_8UC1)
        {
            return mat.Clone();
        }

        var u8 = new Mat();
        var scale = mat.Type() == MatType.CV_16UC1 ? 255.0 / 65535.0 : 255.0;
        mat.ConvertTo(u8, MatType.CV_8UC1, scale);
        return u8;
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

    private static string NormalizeDetector(string detector)
    {
        var normalized = detector.ToLowerInvariant();
        return normalized is "sift" or "orb"
            ? normalized
            : throw new ArgumentException("Detector must be 'sift' or 'orb'.", nameof(detector));
    }
}
