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
            var hasRed = maskRed[i] > 0;
            var hasGreen = maskGreen[i] > 0;
            var hasBlue = maskBlue[i] > 0;
            var r = red.Pixels[i];
            var g = green.Pixels[i];
            var b = blue.Pixels[i];

            if (hasRed && hasGreen && hasBlue)
            {
                rgbPixels[i * 3] = r;
                rgbPixels[(i * 3) + 1] = g;
                rgbPixels[(i * 3) + 2] = b;
                overlap[i] = 1;
                continue;
            }

            overlap[i] = 0;
            var gray = GrayscaleFromAvailableChannels(r, g, b, hasRed, hasGreen, hasBlue);
            rgbPixels[i * 3] = gray;
            rgbPixels[(i * 3) + 1] = gray;
            rgbPixels[(i * 3) + 2] = gray;
        }

        return (new RgbImageBuffer(red.Width, red.Height, rgbPixels), overlap);
    }

    public static float GrayscaleFromAvailableChannels(
        float red,
        float green,
        float blue,
        bool hasRed,
        bool hasGreen,
        bool hasBlue)
    {
        const float weightRed = 0.299f;
        const float weightGreen = 0.587f;
        const float weightBlue = 0.114f;

        var sum = 0.0f;
        var weight = 0.0f;
        if (hasRed)
        {
            sum += weightRed * red;
            weight += weightRed;
        }

        if (hasGreen)
        {
            sum += weightGreen * green;
            weight += weightGreen;
        }

        if (hasBlue)
        {
            sum += weightBlue * blue;
            weight += weightBlue;
        }

        return weight > 0.0f ? sum / weight : 0.0f;
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

    public static (int X0, int Y0, int X1, int Y1)? NonBlackBoundingBox(
        RgbImageBuffer rgb,
        float threshold = 5.0f / 255.0f)
    {
        var found = false;
        var minX = rgb.Width;
        var minY = rgb.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < rgb.Height; y++)
        {
            for (var x = 0; x < rgb.Width; x++)
            {
                var index = (y * rgb.Width) + x;
                var pixelIndex = index * 3;
                var luminance = Math.Max(
                    rgb.Pixels[pixelIndex],
                    Math.Max(rgb.Pixels[pixelIndex + 1], rgb.Pixels[pixelIndex + 2]));
                if (luminance <= threshold)
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

    public static (RgbImageBuffer Image, CropInfo Info) CropToContent(
        RgbImageBuffer rgb,
        byte[] overlap,
        float blackThreshold = 5.0f / 255.0f)
    {
        var overlapBbox = OverlapBoundingBox(overlap, rgb.Width, rgb.Height)
            ?? throw new InvalidOperationException("No overlap between aligned channels; cannot crop.");

        var contentBbox = NonBlackBoundingBox(rgb, blackThreshold)
            ?? throw new InvalidOperationException("Image contains no non-black content; cannot crop.");

        var (x0, y0, x1, y1) = contentBbox;
        var width = x1 - x0;
        var height = y1 - y0;
        var pixels = new float[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var source = (((y0 + y) * rgb.Width) + x0) * 3;
            var target = y * width * 3;
            Array.Copy(rgb.Pixels, source, pixels, target, width * 3);
        }

        var (ox0, oy0, ox1, oy1) = overlapBbox;
        return (
            new RgbImageBuffer(width, height, pixels),
            new CropInfo(x0, y0, x1, y1, ox0, oy0, ox1, oy1));
    }

    public static byte[] CropMask(byte[] mask, int sourceWidth, int sourceHeight, CropInfo crop)
    {
        var width = crop.X1 - crop.X0;
        var height = crop.Y1 - crop.Y0;
        if (mask.Length != sourceWidth * sourceHeight)
        {
            throw new ArgumentException("Mask dimensions must match the source image.");
        }

        var cropped = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(mask, ((crop.Y0 + y) * sourceWidth) + crop.X0, cropped, y * width, width);
        }

        return cropped;
    }

    public static RgbImageBuffer EnforceGrayscaleOutsideOverlap(RgbImageBuffer rgb, byte[] overlap)
    {
        if (overlap.Length != rgb.Width * rgb.Height)
        {
            throw new ArgumentException("Overlap mask must match the image dimensions.");
        }

        var output = rgb.Clone();
        for (var i = 0; i < overlap.Length; i++)
        {
            if (overlap[i] != 0)
            {
                continue;
            }

            var pixelIndex = i * 3;
            var gray = GrayscaleFromAvailableChannels(
                output.Pixels[pixelIndex],
                output.Pixels[pixelIndex + 1],
                output.Pixels[pixelIndex + 2],
                hasRed: true,
                hasGreen: true,
                hasBlue: true);
            output.Pixels[pixelIndex] = gray;
            output.Pixels[pixelIndex + 1] = gray;
            output.Pixels[pixelIndex + 2] = gray;
        }

        return output;
    }
}
