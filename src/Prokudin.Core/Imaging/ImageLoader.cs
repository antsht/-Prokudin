using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        using var image = await Image.LoadAsync<RgbaVector>(stream, cancellationToken);
        var pixels = new float[image.Width * image.Height];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    pixels[(y * image.Width) + x] = Math.Clamp(
                        (0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B),
                        0.0f,
                        1.0f);
                }
            }
        });

        return new ImageBuffer(image.Width, image.Height, pixels);
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

    public static async Task SavePngAsync(string path, RgbImageBuffer rgb, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        using var image = new Image<Rgb24>(rgb.Width, rgb.Height);
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

        await image.SaveAsPngAsync(path, cancellationToken);
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }
}
