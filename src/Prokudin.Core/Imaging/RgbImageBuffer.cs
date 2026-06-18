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
}
