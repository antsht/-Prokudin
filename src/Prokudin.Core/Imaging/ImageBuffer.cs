namespace Prokudin.Core.Imaging;

public sealed class ImageBuffer
{
    public ImageBuffer(int width, int height, float[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel buffer length must equal width * height.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Pixels { get; }

    public float this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    public ImageBuffer Clone()
    {
        return new ImageBuffer(Width, Height, (float[])Pixels.Clone());
    }

    public ImageBuffer Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > Width || y + height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangle is outside the image.");
        }

        var cropped = new float[width * height];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(Pixels, ((y + row) * Width) + x, cropped, row * width, width);
        }

        return new ImageBuffer(width, height, cropped);
    }

    public static ImageBuffer Filled(int width, int height, float value)
    {
        var pixels = new float[width * height];
        Array.Fill(pixels, Math.Clamp(value, 0.0f, 1.0f));
        return new ImageBuffer(width, height, pixels);
    }
}
