namespace Prokudin.Core.Retouch;

/// <summary>
/// A compact, crop-safe per-pixel provenance sidecar for a working channel.
/// It intentionally contains no image data and can therefore be captured with
/// editor history and persisted separately from the native TIFF samples.
/// </summary>
public sealed class RetouchProvenanceMap
{
    private readonly byte[] values;

    public RetouchProvenanceMap(int width, int height, RetouchProvenance initial = RetouchProvenance.Original)
        : this(width, height, CreateValues(width, height, initial))
    {
    }

    public RetouchProvenanceMap(int width, int height, byte[] values)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (values.Length != width * height)
        {
            throw new ArgumentException("Provenance dimensions must match the value count.", nameof(values));
        }

        Width = width;
        Height = height;
        this.values = values;
    }

    public int Width { get; }

    public int Height { get; }

    public int PixelCount => values.Length;

    public RetouchProvenance this[int index]
    {
        get => (RetouchProvenance)values[index];
        set => values[index] = (byte)value;
    }

    public ReadOnlyMemory<byte> Values => values;

    public byte[] ToArray() => (byte[])values.Clone();

    public RetouchProvenanceMap Clone() => new(Width, Height, ToArray());

    public RetouchProvenanceMap Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > Width || y + height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangle is outside provenance bounds.");
        }

        var cropped = new byte[width * height];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(values, ((y + row) * Width) + x, cropped, row * width, width);
        }

        return new RetouchProvenanceMap(width, height, cropped);
    }

    public void Mark(byte[] mask, RetouchProvenance provenance)
    {
        if (mask.Length != values.Length)
        {
            throw new ArgumentException("Mask dimensions must match provenance dimensions.", nameof(mask));
        }

        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] > 0)
            {
                values[i] = (byte)provenance;
            }
        }
    }

    public void MarkIndexes(IEnumerable<int> indexes, RetouchProvenance provenance)
    {
        foreach (var index in indexes)
        {
            values[index] = (byte)provenance;
        }
    }

    public static RetouchProvenanceMap Unknown(int width, int height) =>
        new(width, height, RetouchProvenance.Unknown);

    private static byte[] CreateValues(int width, int height, RetouchProvenance initial)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height));
        }

        var result = new byte[width * height];
        Array.Fill(result, (byte)initial);
        return result;
    }
}
