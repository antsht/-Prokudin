using Prokudin.Core.Imaging;

namespace Prokudin.Core.Transform;

public static class ImageTransformer
{
    public static (ImageBuffer Image, byte[] Mask) ApplyManualTransforms(ImageBuffer image, byte[] mask, ManualTransform transform)
    {
        if (mask.Length != image.Width * image.Height)
        {
            throw new ArgumentException("Mask dimensions must match the image.", nameof(mask));
        }

        if (transform.IsIdentity)
        {
            return (image, mask);
        }

        var output = new float[image.Width * image.Height];
        var outputMask = new byte[image.Width * image.Height];
        var cx = image.Width / 2.0f;
        var cy = image.Height / 2.0f;
        var angle = transform.AngleDegrees * MathF.PI / 180.0f;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var tx = x - cx - transform.Dx;
                var ty = y - cy - transform.Dy;
                var sourceX = (cos * tx) + (sin * ty) + cx;
                var sourceY = (-sin * tx) + (cos * ty) + cy;
                var index = (y * image.Width) + x;
                output[index] = SampleBilinear(image, sourceX, sourceY);
                outputMask[index] = SampleNearest(mask, image.Width, image.Height, sourceX, sourceY);
            }
        }

        return (new ImageBuffer(image.Width, image.Height, output), outputMask);
    }

    private static float SampleBilinear(ImageBuffer image, float x, float y)
    {
        if (x < 0 || y < 0 || x > image.Width - 1 || y > image.Height - 1)
        {
            return 0.0f;
        }

        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var tx = x - x0;
        var ty = y - y0;

        var top = Lerp(image[x0, y0], image[x1, y0], tx);
        var bottom = Lerp(image[x0, y1], image[x1, y1], tx);
        return Lerp(top, bottom, ty);
    }

    private static byte SampleNearest(byte[] mask, int width, int height, float x, float y)
    {
        var ix = (int)MathF.Round(x);
        var iy = (int)MathF.Round(y);
        if (ix < 0 || iy < 0 || ix >= width || iy >= height)
        {
            return 0;
        }

        return mask[(iy * width) + ix] > 0 ? (byte)1 : (byte)0;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
}
