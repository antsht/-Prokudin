using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Compression.Zlib;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.PixelFormats;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Imaging;

public static class ImageLoader
{
    public static readonly ISet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
    };

    public static bool IsSupportedImagePath(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static async Task<ImageBuffer> LoadGrayscaleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        var extension = Path.GetExtension(path);
        if (!SupportedExtensions.Contains(extension))
        {
            var supported = string.Join(", ", SupportedExtensions.Order(StringComparer.OrdinalIgnoreCase));
            throw new NotSupportedException($"Unsupported format '{extension}' for {Path.GetFileName(path)}. Use: {supported}");
        }

        await using var stream = File.OpenRead(path);
        if (extension is ".tif" or ".tiff")
        {
            using var l16 = await Image.LoadAsync<L16>(stream, cancellationToken);
            var is16Bit = false;
            l16.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height && !is16Bit; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].PackedValue > 255)
                        {
                            is16Bit = true;
                            return;
                        }
                    }
                }
            });

            if (is16Bit)
            {
                var pixels = new ushort[l16.Width * l16.Height];
                l16.ProcessPixelRows(accessor =>
                {
                    for (var y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (var x = 0; x < row.Length; x++)
                        {
                            pixels[(y * l16.Width) + x] = row[x].PackedValue;
                        }
                    }
                });

                return new ImageBuffer(l16.Width, l16.Height, pixels);
            }
        }

        stream.Position = 0;
        using var image = await Image.LoadAsync<RgbaVector>(stream, cancellationToken);
        var floatPixels = new float[image.Width * image.Height];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    floatPixels[(y * image.Width) + x] = Math.Clamp(
                        (0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B),
                        0.0f,
                        1.0f);
                }
            }
        });

        return new ImageBuffer(image.Width, image.Height, floatPixels);
    }

    public static ImageBuffer TrimBlackBorders(ImageBuffer image, float threshold = 5.0f / 255.0f, float maxTrimFraction = 0.02f)
    {
        var maxVertical = (int)(image.Height * maxTrimFraction);
        var maxHorizontal = (int)(image.Width * maxTrimFraction);

        bool IsDarkRow(int y)
        {
            var sum = 0.0f;
            var max = 0.0f;
            for (var x = 0; x < image.Width; x++)
            {
                var value = image[x, y];
                sum += value;
                max = Math.Max(max, value);
            }

            return (sum / image.Width) < threshold && max < threshold * 2;
        }

        bool IsDarkColumn(int x)
        {
            var sum = 0.0f;
            var max = 0.0f;
            for (var y = 0; y < image.Height; y++)
            {
                var value = image[x, y];
                sum += value;
                max = Math.Max(max, value);
            }

            return (sum / image.Height) < threshold && max < threshold * 2;
        }

        var top = 0;
        for (var y = 0; y < maxVertical && IsDarkRow(y); y++)
        {
            top++;
        }

        var bottom = 0;
        for (var y = image.Height - 1; y >= image.Height - maxVertical && IsDarkRow(y); y--)
        {
            bottom++;
        }

        var left = 0;
        for (var x = 0; x < maxHorizontal && IsDarkColumn(x); x++)
        {
            left++;
        }

        var right = 0;
        for (var x = image.Width - 1; x >= image.Width - maxHorizontal && IsDarkColumn(x); x--)
        {
            right++;
        }

        if (top + bottom + left + right == 0)
        {
            return image;
        }

        var width = image.Width - left - right;
        var height = image.Height - top - bottom;
        return width <= 0 || height <= 0 ? image : image.Crop(left, top, width, height);
    }

    public static Task SavePngAsync(string path, RgbImageBuffer rgb, CancellationToken cancellationToken = default)
    {
        return SaveRgbAsync(path, rgb, RgbExportSettings.Default, cancellationToken);
    }

    public static async Task SaveRgbAsync(
        string path,
        RgbImageBuffer rgb,
        RgbExportSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings = settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var output = ResizeToMaxSide(rgb, settings.MaxSide);
        using var image = ToImage(output);

        switch (settings.Format)
        {
            case RgbExportFormat.Png:
                await image.SaveAsPngAsync(
                    path,
                    new PngEncoder { CompressionLevel = ToPngCompressionLevel(settings.PngCompression) },
                    cancellationToken);
                break;
            case RgbExportFormat.Jpeg:
                await image.SaveAsJpegAsync(
                    path,
                    new JpegEncoder { Quality = settings.JpegQuality },
                    cancellationToken);
                break;
            case RgbExportFormat.Tiff:
                await image.SaveAsTiffAsync(
                    path,
                    new TiffEncoder
                    {
                        Compression = ToTiffCompression(settings.TiffCompression),
                        CompressionLevel = ToDeflateCompressionLevel(settings.TiffDeflateLevel),
                    },
                    cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported RGB export format: {settings.Format}");
        }
    }

    private static Image<Rgb24> ToImage(RgbImageBuffer rgb)
    {
        var image = new Image<Rgb24>(rgb.Width, rgb.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < rgb.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < rgb.Width; x++)
                {
                    row[x] = new Rgb24(
                        ToByte(rgb[x, y, 0]),
                        ToByte(rgb[x, y, 1]),
                        ToByte(rgb[x, y, 2]));
                }
            }
        });

        return image;
    }

    public static async Task SaveGrayscalePngAsync(string path, ImageBuffer imageBuffer, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        using var image = new Image<L8>(imageBuffer.Width, imageBuffer.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < imageBuffer.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < imageBuffer.Width; x++)
                {
                    row[x] = new L8(ToByte(imageBuffer[x, y]));
                }
            }
        });

        await image.SaveAsPngAsync(path, cancellationToken);
    }

    public static Task SaveGrayscaleTiffAsync(
        string path,
        ImageBuffer imageBuffer,
        CancellationToken cancellationToken = default)
    {
        return SaveGrayscaleTiffAsync(path, imageBuffer, TiffExportCompression.Deflate, 6, cancellationToken);
    }

    public static async Task SaveGrayscaleTiffAsync(
        string path,
        ImageBuffer imageBuffer,
        TiffExportCompression compression,
        int deflateLevel,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var encoder = new TiffEncoder
        {
            Compression = ToTiffCompression(compression),
            CompressionLevel = ToDeflateCompressionLevel(deflateLevel),
        };

        if (imageBuffer.Format == PixelFormat.UInt16)
        {
            var pixels = imageBuffer.UInt16Pixels;
            using var image = new Image<L16>(imageBuffer.Width, imageBuffer.Height);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < imageBuffer.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var rowOffset = y * imageBuffer.Width;
                    for (var x = 0; x < imageBuffer.Width; x++)
                    {
                        row[x] = new L16(pixels[rowOffset + x]);
                    }
                }
            });

            await image.SaveAsTiffAsync(path, encoder, cancellationToken);
            return;
        }

        using var image8 = new Image<L8>(imageBuffer.Width, imageBuffer.Height);
        image8.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < imageBuffer.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < imageBuffer.Width; x++)
                {
                    row[x] = new L8(ToByte(imageBuffer[x, y]));
                }
            }
        });

        await image8.SaveAsTiffAsync(path, encoder, cancellationToken);
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }

    private static RgbImageBuffer ResizeToMaxSide(RgbImageBuffer source, int? maxSide)
    {
        if (maxSide is not { } limit || limit <= 0)
        {
            return source;
        }

        var currentMax = Math.Max(source.Width, source.Height);
        if (currentMax <= limit)
        {
            return source;
        }

        var scale = limit / (double)currentMax;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return ResizeRgb(source, width, height);
    }

    private static RgbImageBuffer ResizeRgb(RgbImageBuffer source, int width, int height)
    {
        var pixels = new float[width * height * 3];
        var scaleX = source.Width / (float)width;
        var scaleY = source.Height / (float)height;
        PixelParallel.ForRows(height, y =>
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y * scaleY));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x * scaleX));
                for (var c = 0; c < 3; c++)
                {
                    pixels[(((y * width) + x) * 3) + c] = source[sourceX, sourceY, c];
                }
            }
        });

        return new RgbImageBuffer(width, height, pixels);
    }

    private static PngCompressionLevel ToPngCompressionLevel(int level)
    {
        return Math.Clamp(level, 0, 9) switch
        {
            0 => PngCompressionLevel.Level0,
            1 => PngCompressionLevel.Level1,
            2 => PngCompressionLevel.Level2,
            3 => PngCompressionLevel.Level3,
            4 => PngCompressionLevel.Level4,
            5 => PngCompressionLevel.Level5,
            6 => PngCompressionLevel.Level6,
            7 => PngCompressionLevel.Level7,
            8 => PngCompressionLevel.Level8,
            _ => PngCompressionLevel.Level9,
        };
    }

    private static TiffCompression ToTiffCompression(TiffExportCompression compression)
    {
        return compression switch
        {
            TiffExportCompression.None => TiffCompression.None,
            TiffExportCompression.Deflate => TiffCompression.Deflate,
            _ => TiffCompression.Lzw,
        };
    }

    private static DeflateCompressionLevel ToDeflateCompressionLevel(int level)
    {
        return (DeflateCompressionLevel)Math.Clamp(level, 1, 9);
    }
}
