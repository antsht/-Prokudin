using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Prokudin.Core.Imaging;

namespace Prokudin.Gui.Imaging;

public static class AvaloniaBitmapFactory
{
    private static readonly Vector Dpi = new(96, 96);

    public static WriteableBitmap FromImageBuffer(ImageBuffer image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            var value = ToByte(image.Pixels[i]);
            var offset = i * 4;
            bytes[offset] = value;
            bytes[offset + 1] = value;
            bytes[offset + 2] = value;
            bytes[offset + 3] = 255;
        }

        return CreateBitmap(image.Width, image.Height, bytes);
    }

    public static WriteableBitmap FromRgbImageBuffer(RgbImageBuffer image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        for (var i = 0; i < image.Width * image.Height; i++)
        {
            var sourceOffset = i * 3;
            var targetOffset = i * 4;
            bytes[targetOffset] = ToByte(image.Pixels[sourceOffset + 2]);
            bytes[targetOffset + 1] = ToByte(image.Pixels[sourceOffset + 1]);
            bytes[targetOffset + 2] = ToByte(image.Pixels[sourceOffset]);
            bytes[targetOffset + 3] = 255;
        }

        return CreateBitmap(image.Width, image.Height, bytes);
    }

    public static WriteableBitmap FromMaskOverlay(byte[] mask, int width, int height)
    {
        if (mask.Length != width * height)
        {
            throw new ArgumentException("Mask dimensions must match width * height.", nameof(mask));
        }

        const byte alpha = 116;
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0)
            {
                continue;
            }

            var offset = i * 4;
            bytes[offset] = 18;
            bytes[offset + 1] = 42;
            bytes[offset + 2] = 116;
            bytes[offset + 3] = alpha;
        }

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
            PixelFormat.Bgra8888,
            alphaFormat);

        using var buffer = bitmap.Lock();
        Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
        return bitmap;
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }
}
