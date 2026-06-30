using System.Runtime.InteropServices;
using OpenCvSharp;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Imaging;

public static class ImageMatConverter
{
    public static Mat ToMat(ImageBuffer image)
    {
        return image.Format switch
        {
            PixelFormat.Float32 => ToFloatMat(image),
            PixelFormat.UInt8 => ToUInt8Mat(image),
            PixelFormat.UInt16 => ToUInt16Mat(image),
            _ => throw new InvalidOperationException($"Unsupported format {image.Format}."),
        };
    }

    public static ImageBuffer FromMat(Mat mat, PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Float32 => FromFloatMat(mat),
            PixelFormat.UInt8 => FromUInt8Mat(mat),
            PixelFormat.UInt16 => FromUInt16Mat(mat),
            _ => throw new InvalidOperationException($"Unsupported format {format}."),
        };
    }

    public static Mat ToNormalizedFloatMat(ImageBuffer image)
    {
        var mat = new Mat(image.Height, image.Width, MatType.CV_32FC1);
        var pixels = new float[image.PixelCount];
        image.CopyNormalizedTo(pixels);
        Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
        return mat;
    }

    public static Mat ToUInt8MatForInpaint(ImageBuffer image)
    {
        if (image.Format == PixelFormat.UInt8)
        {
            return ToUInt8Mat(image);
        }

        using var source = ToMat(image);
        var u8 = new Mat();
        var scale = image.Format == PixelFormat.Float32 ? 255.0 : 255.0 / 65535.0;
        source.ConvertTo(u8, MatType.CV_8UC1, scale);
        return u8;
    }

    private static Mat ToFloatMat(ImageBuffer image)
    {
        var mat = new Mat(image.Height, image.Width, MatType.CV_32FC1);
        Marshal.Copy(image.Pixels, 0, mat.Data, image.PixelCount);
        return mat;
    }

    private static Mat ToUInt8Mat(ImageBuffer image)
    {
        var mat = new Mat(image.Height, image.Width, MatType.CV_8UC1);
        Marshal.Copy(image.UInt8Pixels, 0, mat.Data, image.PixelCount);
        return mat;
    }

    private static Mat ToUInt16Mat(ImageBuffer image)
    {
        using var normalized = ToNormalizedFloatMat(image);
        var u16 = new Mat();
        normalized.ConvertTo(u16, MatType.CV_16UC1, 65535.0);
        return u16;
    }

    private static ImageBuffer FromFloatMat(Mat mat)
    {
        using var floatMat = new Mat();
        var scale = mat.Type() switch
        {
            MatType t when t == MatType.CV_8UC1 => 1.0 / 255.0,
            MatType t when t == MatType.CV_16UC1 => 1.0 / 65535.0,
            _ => 1.0,
        };
        mat.ConvertTo(floatMat, MatType.CV_32FC1, scale);
        var pixels = new float[floatMat.Rows * floatMat.Cols];
        Marshal.Copy(floatMat.Data, pixels, 0, pixels.Length);
        PixelParallel.For(0, pixels.Length, i =>
        {
            pixels[i] = Math.Clamp(pixels[i], 0.0f, 1.0f);
        });

        return new ImageBuffer(floatMat.Cols, floatMat.Rows, pixels);
    }

    private static ImageBuffer FromUInt8Mat(Mat mat)
    {
        using var u8 = new Mat();
        mat.ConvertTo(u8, MatType.CV_8UC1);
        var pixels = new byte[u8.Rows * u8.Cols];
        Marshal.Copy(u8.Data, pixels, 0, pixels.Length);
        return new ImageBuffer(u8.Cols, u8.Rows, pixels);
    }

    private static ImageBuffer FromUInt16Mat(Mat mat)
    {
        using var normalized = new Mat();
        var scale = mat.Type() switch
        {
            MatType t when t == MatType.CV_8UC1 => 1.0 / 255.0,
            MatType t when t == MatType.CV_16UC1 => 1.0 / 65535.0,
            _ => 1.0,
        };
        mat.ConvertTo(normalized, MatType.CV_32FC1, scale);
        var floats = new float[normalized.Rows * normalized.Cols];
        Marshal.Copy(normalized.Data, floats, 0, floats.Length);
        return ImageBuffer.FromNormalized(normalized.Cols, normalized.Rows, floats, PixelFormat.UInt16);
    }
}
