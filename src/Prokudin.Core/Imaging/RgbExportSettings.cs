namespace Prokudin.Core.Imaging;

public enum RgbExportFormat
{
    Png,
    Jpeg,
    Tiff,
}

public enum TiffExportCompression
{
    None,
    Lzw,
    Deflate,
}

public sealed record RgbExportSettings
{
    public static RgbExportSettings Default { get; } = new();

    public RgbExportFormat Format { get; init; } = RgbExportFormat.Png;

    public int? MaxSide { get; init; }

    public int PngCompression { get; init; } = 6;

    public int JpegQuality { get; init; } = 90;

    public TiffExportCompression TiffCompression { get; init; } = TiffExportCompression.Lzw;

    public int TiffDeflateLevel { get; init; } = 6;

    public string DefaultExtension => Format switch
    {
        RgbExportFormat.Png => "png",
        RgbExportFormat.Jpeg => "jpg",
        RgbExportFormat.Tiff => "tif",
        _ => "png",
    };

    public RgbExportSettings Normalize()
    {
        return this with
        {
            MaxSide = MaxSide is > 0 ? MaxSide : null,
            PngCompression = Math.Clamp(PngCompression, 0, 9),
            JpegQuality = Math.Clamp(JpegQuality, 1, 100),
            TiffDeflateLevel = Math.Clamp(TiffDeflateLevel, 1, 9),
        };
    }
}
