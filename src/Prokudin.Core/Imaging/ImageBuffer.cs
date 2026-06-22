namespace Prokudin.Core.Imaging;

public sealed class ImageBuffer
{
    private readonly Array _pixels;

    public ImageBuffer(int width, int height, float[] pixels)
        : this(width, height, PixelFormat.Float32, pixels)
    {
    }

    public ImageBuffer(int width, int height, byte[] pixels)
        : this(width, height, PixelFormat.UInt8, pixels)
    {
    }

    public ImageBuffer(int width, int height, ushort[] pixels)
        : this(width, height, PixelFormat.UInt16, pixels)
    {
    }

    private ImageBuffer(int width, int height, PixelFormat format, Array pixels)
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
        Format = format;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public PixelFormat Format { get; }

    public int PixelCount => Width * Height;

    public float[] Pixels =>
        Format == PixelFormat.Float32
            ? (float[])_pixels
            : throw new InvalidOperationException(
                $"Direct float[] access is only valid for {PixelFormat.Float32}. Use normalized accessors.");

    public byte[] UInt8Pixels =>
        Format == PixelFormat.UInt8
            ? (byte[])_pixels
            : throw new InvalidOperationException($"Buffer is {Format}, not {PixelFormat.UInt8}.");

    public ushort[] UInt16Pixels =>
        Format == PixelFormat.UInt16
            ? (ushort[])_pixels
            : throw new InvalidOperationException($"Buffer is {Format}, not {PixelFormat.UInt16}.");

    public float this[int x, int y]
    {
        get => GetNormalized(ToIndex(x, y));
        set => SetNormalized(ToIndex(x, y), value);
    }

    public float GetNormalized(int index)
    {
        return Format switch
        {
            PixelFormat.Float32 => Math.Clamp(((float[])_pixels)[index], 0.0f, 1.0f),
            PixelFormat.UInt8 => ((byte[])_pixels)[index] / 255.0f,
            PixelFormat.UInt16 => ((ushort[])_pixels)[index] / 65535.0f,
            _ => throw new InvalidOperationException($"Unsupported format {Format}."),
        };
    }

    public void SetNormalized(int index, float value)
    {
        switch (Format)
        {
            case PixelFormat.Float32:
                ((float[])_pixels)[index] = Math.Clamp(value, 0.0f, 1.0f);
                break;
            case PixelFormat.UInt8:
                ((byte[])_pixels)[index] = (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
                break;
            case PixelFormat.UInt16:
                ((ushort[])_pixels)[index] = (ushort)Math.Clamp((int)MathF.Round(value * 65535.0f), 0, 65535);
                break;
            default:
                throw new InvalidOperationException($"Unsupported format {Format}.");
        }
    }

    public void CopyNormalizedTo(Span<float> destination)
    {
        if (destination.Length < PixelCount)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        for (var i = 0; i < PixelCount; i++)
        {
            destination[i] = GetNormalized(i);
        }
    }

    public void CopyNormalizedTo(float[] destination) => CopyNormalizedTo(destination.AsSpan());

    public ImageBuffer Clone()
    {
        return Format switch
        {
            PixelFormat.Float32 => new ImageBuffer(Width, Height, (float[])((float[])_pixels).Clone()),
            PixelFormat.UInt8 => new ImageBuffer(Width, Height, (byte[])((byte[])_pixels).Clone()),
            PixelFormat.UInt16 => new ImageBuffer(Width, Height, (ushort[])((ushort[])_pixels).Clone()),
            _ => throw new InvalidOperationException($"Unsupported format {Format}."),
        };
    }

    public ImageBuffer Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > Width || y + height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangle is outside the image.");
        }

        return Format switch
        {
            PixelFormat.Float32 => new ImageBuffer(width, height, CropArray((float[])_pixels, x, y, width, height)),
            PixelFormat.UInt8 => new ImageBuffer(width, height, CropArray((byte[])_pixels, x, y, width, height)),
            PixelFormat.UInt16 => new ImageBuffer(width, height, CropArray((ushort[])_pixels, x, y, width, height)),
            _ => throw new InvalidOperationException($"Unsupported format {Format}."),
        };
    }

    public ImageBuffer WithFormat(PixelFormat targetFormat)
    {
        if (Format == targetFormat)
        {
            return Clone();
        }

        var normalized = new float[PixelCount];
        CopyNormalizedTo(normalized);
        return FromNormalized(Width, Height, normalized, targetFormat);
    }

    public static ImageBuffer Filled(int width, int height, float value, PixelFormat format = PixelFormat.Float32)
    {
        var count = width * height;
        return format switch
        {
            PixelFormat.Float32 => new ImageBuffer(width, height, CreateFilled(count, Math.Clamp(value, 0.0f, 1.0f))),
            PixelFormat.UInt8 => new ImageBuffer(
                width,
                height,
                CreateFilled(count, (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255))),
            PixelFormat.UInt16 => new ImageBuffer(
                width,
                height,
                CreateFilled(count, (ushort)Math.Clamp((int)MathF.Round(value * 65535.0f), 0, 65535))),
            _ => throw new InvalidOperationException($"Unsupported format {format}."),
        };
    }

    public static ImageBuffer FromNormalized(int width, int height, float[] normalizedPixels, PixelFormat format)
    {
        if (normalizedPixels.Length != width * height)
        {
            throw new ArgumentException("Pixel buffer length must equal width * height.", nameof(normalizedPixels));
        }

        return format switch
        {
            PixelFormat.Float32 => new ImageBuffer(
                width,
                height,
                normalizedPixels.Select(static v => Math.Clamp(v, 0.0f, 1.0f)).ToArray()),
            PixelFormat.UInt8 => new ImageBuffer(
                width,
                height,
                normalizedPixels.Select(static v => (byte)Math.Clamp((int)MathF.Round(v * 255.0f), 0, 255)).ToArray()),
            PixelFormat.UInt16 => new ImageBuffer(
                width,
                height,
                normalizedPixels.Select(static v => (ushort)Math.Clamp((int)MathF.Round(v * 65535.0f), 0, 65535)).ToArray()),
            _ => throw new InvalidOperationException($"Unsupported format {format}."),
        };
    }

    public float MaxAllowedHealError =>
        Format switch
        {
            PixelFormat.Float32 => 0.12f,
            PixelFormat.UInt8 => 30.0f / 255.0f,
            PixelFormat.UInt16 => 7700.0f / 65535.0f,
            _ => 0.12f,
        };

    private static int ToIndex(int x, int y, int width) => (y * width) + x;

    private int ToIndex(int x, int y) => ToIndex(x, y, Width);

    private static T[] CropArray<T>(T[] source, int x, int y, int width, int height, int sourceWidth)
    {
        var cropped = new T[width * height];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(source, ((y + row) * sourceWidth) + x, cropped, row * width, width);
        }

        return cropped;
    }

    private T[] CropArray<T>(T[] source, int x, int y, int width, int height) =>
        CropArray(source, x, y, width, height, Width);

    private static T[] CreateFilled<T>(int count, T value)
    {
        var pixels = new T[count];
        Array.Fill(pixels, value);
        return pixels;
    }
}
