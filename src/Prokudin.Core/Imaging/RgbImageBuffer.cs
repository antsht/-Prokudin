namespace Prokudin.Core.Imaging;

public sealed class RgbImageBuffer
{
    public RgbImageBuffer(int width, int height, float[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (pixels.Length != width * height * 3)
        {
            throw new ArgumentException("RGB buffer length must equal width * height * 3.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Pixels { get; }

    public float this[int x, int y, int channel]
    {
        get => Pixels[(((y * Width) + x) * 3) + channel];
        set => Pixels[(((y * Width) + x) * 3) + channel] = value;
    }

    public RgbImageBuffer Clone()
    {
        return new RgbImageBuffer(Width, Height, (float[])Pixels.Clone());
    }

    public RgbImageBuffer Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > Width || y + height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangle is outside the image.");
        }

        var cropped = new float[width * height * 3];
        for (var row = 0; row < height; row++)
        {
            var sourceRow = ((y + row) * Width) + x;
            var destRow = row * width;
            for (var col = 0; col < width; col++)
            {
                var sourceIndex = (sourceRow + col) * 3;
                var destIndex = (destRow + col) * 3;
                cropped[destIndex] = Pixels[sourceIndex];
                cropped[destIndex + 1] = Pixels[sourceIndex + 1];
                cropped[destIndex + 2] = Pixels[sourceIndex + 2];
            }
        }

        return new RgbImageBuffer(width, height, cropped);
    }
}
