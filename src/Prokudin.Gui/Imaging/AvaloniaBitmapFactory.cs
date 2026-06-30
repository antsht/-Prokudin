using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Prokudin.Core.Imaging;
using Prokudin.Core.Processing;

namespace Prokudin.Gui.Imaging;

public static class AvaloniaBitmapFactory
{
    public const int DefaultThumbnailMaxSide = 512;

    private static readonly Vector Dpi = new(96, 96);

    public static WriteableBitmap FromImageBuffer(ImageBuffer image)
    {
        var snapshotSource = image.Clone();
        var snapshot = new float[snapshotSource.PixelCount];
        snapshotSource.CopyNormalizedTo(snapshot.AsSpan());

        var bytes = new byte[image.Width * image.Height * 4];
        PixelParallel.For(0, snapshot.Length, i =>
        {
            var value = ToByte(snapshot[i]);
            var offset = i * 4;
            bytes[offset] = value;
            bytes[offset + 1] = value;
            bytes[offset + 2] = value;
            bytes[offset + 3] = 255;
        });

        return CreateBitmap(image.Width, image.Height, bytes);
    }

    public static WriteableBitmap FromRgbImageBuffer(RgbImageBuffer image)
    {
        var pixels = (float[])image.Pixels.Clone();
        var bytes = new byte[image.Width * image.Height * 4];
        PixelParallel.For(0, image.Width * image.Height, i =>
        {
            var sourceOffset = i * 3;
            var targetOffset = i * 4;
            bytes[targetOffset] = ToByte(pixels[sourceOffset + 2]);
            bytes[targetOffset + 1] = ToByte(pixels[sourceOffset + 1]);
            bytes[targetOffset + 2] = ToByte(pixels[sourceOffset]);
            bytes[targetOffset + 3] = 255;
        });

        return CreateBitmap(image.Width, image.Height, bytes);
    }

    public static WriteableBitmap CreateThumbnail(ImageBuffer image, int maxSide = DefaultThumbnailMaxSide)
    {
        var (width, height) = FitWithinMaxSide(image.Width, image.Height, maxSide);
        if (width == image.Width && height == image.Height)
        {
            return FromImageBuffer(image);
        }

        var bytes = new byte[width * height * 4];
        var snapshotSource = image.Clone();
        var snapshot = new float[snapshotSource.PixelCount];
        snapshotSource.CopyNormalizedTo(snapshot.AsSpan());
        PixelParallel.ForRows(height, y =>
        {
            var sourceY = (y * image.Height) / height;
            for (var x = 0; x < width; x++)
            {
                var sourceX = (x * image.Width) / width;
                var value = ToByte(snapshot[(sourceY * image.Width) + sourceX]);
                var offset = ((y * width) + x) * 4;
                bytes[offset] = value;
                bytes[offset + 1] = value;
                bytes[offset + 2] = value;
                bytes[offset + 3] = 255;
            }
        });

        return CreateBitmap(width, height, bytes);
    }

    public static WriteableBitmap CreateThumbnail(RgbImageBuffer image, int maxSide = DefaultThumbnailMaxSide)
    {
        var (width, height) = FitWithinMaxSide(image.Width, image.Height, maxSide);
        if (width == image.Width && height == image.Height)
        {
            return FromRgbImageBuffer(image);
        }

        var pixels = (float[])image.Pixels.Clone();
        var bytes = new byte[width * height * 4];
        PixelParallel.ForRows(height, y =>
        {
            var sourceY = (y * image.Height) / height;
            for (var x = 0; x < width; x++)
            {
                var sourceX = (x * image.Width) / width;
                var sourceOffset = ((sourceY * image.Width) + sourceX) * 3;
                var targetOffset = ((y * width) + x) * 4;
                bytes[targetOffset] = ToByte(pixels[sourceOffset + 2]);
                bytes[targetOffset + 1] = ToByte(pixels[sourceOffset + 1]);
                bytes[targetOffset + 2] = ToByte(pixels[sourceOffset]);
                bytes[targetOffset + 3] = 255;
            }
        });

        return CreateBitmap(width, height, bytes);
    }

    public static WriteableBitmap FromMaskOverlay(byte[] mask, int width, int height)
    {
        if (mask.Length != width * height)
        {
            throw new ArgumentException("Mask dimensions must match width * height.", nameof(mask));
        }

        const byte alpha = 116;
        var bytes = new byte[width * height * 4];
        PixelParallel.For(0, mask.Length, i =>
        {
            if (mask[i] == 0)
            {
                return;
            }

            var offset = i * 4;
            bytes[offset] = 18;
            bytes[offset + 1] = 42;
            bytes[offset + 2] = 116;
            bytes[offset + 3] = alpha;
        });

        return CreateBitmap(width, height, bytes, AlphaFormat.Premul);
    }

    private static WriteableBitmap CreateBitmap(
        int width,
        int height,
        byte[] bytes,
        AlphaFormat alphaFormat = AlphaFormat.Opaque)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            Dpi,
            Avalonia.Platform.PixelFormat.Bgra8888,
            alphaFormat);

        using var buffer = bitmap.Lock();
        var rowBytes = buffer.RowBytes;
        var sourceStride = width * 4;
        if (rowBytes == sourceStride)
        {
            Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
        }
        else
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(
                    bytes,
                    row * sourceStride,
                    buffer.Address + (row * rowBytes),
                    sourceStride);
            }
        }

        return bitmap;
    }

    private static (int Width, int Height) FitWithinMaxSide(int width, int height, int maxSide)
    {
        if (width <= maxSide && height <= maxSide)
        {
            return (width, height);
        }

        var scale = maxSide / (double)Math.Max(width, height);
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }
}
