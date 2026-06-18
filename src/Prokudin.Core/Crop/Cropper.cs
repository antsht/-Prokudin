using Prokudin.Core.Imaging;

namespace Prokudin.Core.Crop;

public static class Cropper
{
    public static (RgbImageBuffer Rgb, byte[] Overlap) MergeChannels(
        ImageBuffer red,
        ImageBuffer green,
        ImageBuffer blue,
        byte[] maskRed,
        byte[] maskGreen,
        byte[] maskBlue)
    {
        if (red.Width != green.Width || red.Width != blue.Width || red.Height != green.Height || red.Height != blue.Height)
        {
            throw new ArgumentException("All channels must have the same dimensions.");
        }

        var pixelCount = red.Width * red.Height;
        if (maskRed.Length != pixelCount || maskGreen.Length != pixelCount || maskBlue.Length != pixelCount)
        {
            throw new ArgumentException("Masks must match channel dimensions.");
        }

        var rgbPixels = new float[pixelCount * 3];
        var overlap = new byte[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            rgbPixels[i * 3] = red.Pixels[i];
            rgbPixels[(i * 3) + 1] = green.Pixels[i];
            rgbPixels[(i * 3) + 2] = blue.Pixels[i];
            overlap[i] = maskRed[i] > 0 && maskGreen[i] > 0 && maskBlue[i] > 0 ? (byte)1 : (byte)0;
        }

        return (new RgbImageBuffer(red.Width, red.Height, rgbPixels), overlap);
    }

    public static (int X0, int Y0, int X1, int Y1)? OverlapBoundingBox(byte[] overlap, int width, int height)
    {
        var found = false;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (overlap[(y * width) + x] == 0)
                {
                    continue;
                }

                found = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return found ? (minX, minY, maxX + 1, maxY + 1) : null;
    }

    public static (RgbImageBuffer Square, CropInfo Info) SquareCrop(RgbImageBuffer rgb, byte[] overlap)
    {
        var bbox = OverlapBoundingBox(overlap, rgb.Width, rgb.Height);
        if (bbox is null)
        {
            throw new InvalidOperationException("No overlap between aligned channels; cannot crop.");
        }

        var (ox0, oy0, ox1, oy1) = bbox.Value;
        var croppedWidth = ox1 - ox0;
        var croppedHeight = oy1 - oy0;
        var side = Math.Min(croppedWidth, croppedHeight);
        var cropX = Math.Clamp((croppedWidth / 2) - (side / 2), 0, croppedWidth - side);
        var cropY = Math.Clamp((croppedHeight / 2) - (side / 2), 0, croppedHeight - side);
        var x0 = ox0 + cropX;
        var y0 = oy0 + cropY;

        var pixels = new float[side * side * 3];
        for (var y = 0; y < side; y++)
        {
            var source = (((y0 + y) * rgb.Width) + x0) * 3;
            var target = y * side * 3;
            Array.Copy(rgb.Pixels, source, pixels, target, side * 3);
        }

        return (
            new RgbImageBuffer(side, side, pixels),
            new CropInfo(x0, y0, x0 + side, y0 + side, ox0, oy0, ox1, oy1));
    }
}
