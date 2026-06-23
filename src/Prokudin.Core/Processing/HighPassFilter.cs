using System.Runtime.InteropServices;
using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Processing;

internal static class HighPassFilter
{
    public static void Compute(float[] source, int width, int height, double sigma, float[] output)
    {
        if (source.Length != output.Length || source.Length != width * height)
        {
            throw new ArgumentException("Source and output buffers must match image dimensions.");
        }

        using var sourceMat = FloatToMat(source, width, height);
        using var blur = new Mat();
        Cv2.GaussianBlur(sourceMat, blur, new Size(0, 0), sigma);
        var blurred = new float[source.Length];
        Marshal.Copy(blur.Data, blurred, 0, blurred.Length);

        PixelParallel.For(0, source.Length, i =>
        {
            output[i] = Math.Abs(source[i] - blurred[i]);
        });
    }

    private static Mat FloatToMat(float[] source, int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_32FC1);
        Marshal.Copy(source, 0, mat.Data, source.Length);
        return mat;
    }
}
