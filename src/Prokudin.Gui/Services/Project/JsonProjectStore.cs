using System.Text.Json;
using System.Text.Json.Serialization;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Gui.Services.Project;

public sealed class JsonProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public bool IsValidProjectFolder(string folderPath) =>
        File.Exists(Path.Combine(folderPath, ProjectFileNames.Manifest));

    public async Task<ProjectPackage> LoadAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(folderPath, ProjectFileNames.Manifest);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Project manifest not found: {manifestPath}", manifestPath);
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, SerializerOptions)
                       ?? throw new InvalidDataException("Project manifest is empty.");

        if (document.FormatVersion != ProjectDocument.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"Unsupported project format version {document.FormatVersion}. Expected {ProjectDocument.CurrentFormatVersion}.");
        }

        var redPath = Path.Combine(folderPath, ProjectFileNames.RedChannel);
        var greenPath = Path.Combine(folderPath, ProjectFileNames.GreenChannel);
        var bluePath = Path.Combine(folderPath, ProjectFileNames.BlueChannel);
        var resultPath = Path.Combine(folderPath, ProjectFileNames.Result);

        ImageBuffer? red = File.Exists(redPath) ? await ImageLoader.LoadGrayscaleAsync(redPath, cancellationToken) : null;
        ImageBuffer? green = File.Exists(greenPath) ? await ImageLoader.LoadGrayscaleAsync(greenPath, cancellationToken) : null;
        ImageBuffer? blue = File.Exists(bluePath) ? await ImageLoader.LoadGrayscaleAsync(bluePath, cancellationToken) : null;
        RgbImageBuffer? result = await LoadResultAsync(resultPath, cancellationToken);
        var redProvenance = await LoadProvenanceAsync(Path.Combine(folderPath, ProjectFileNames.RedProvenance), red, cancellationToken);
        var greenProvenance = await LoadProvenanceAsync(Path.Combine(folderPath, ProjectFileNames.GreenProvenance), green, cancellationToken);
        var blueProvenance = await LoadProvenanceAsync(Path.Combine(folderPath, ProjectFileNames.BlueProvenance), blue, cancellationToken);

        return new ProjectPackage
        {
            Document = document,
            Red = red,
            Green = green,
            Blue = blue,
            Result = result,
            RedProvenance = redProvenance,
            GreenProvenance = greenProvenance,
            BlueProvenance = blueProvenance,
        };
    }

    public Task SaveAsync(string folderPath, ProjectPackage package, CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(folderPath, package, cancellationToken);
    }

    internal static async Task SaveCoreAsync(
        string folderPath,
        ProjectPackage package,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folderPath);
        var tempFolder = Path.Combine(folderPath, ProjectFileNames.SaveTempFolder);
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, recursive: true);
        }

        Directory.CreateDirectory(tempFolder);

        try
        {
            package.Document.SavedAt = DateTimeOffset.UtcNow;
            package.Document.FormatVersion = ProjectDocument.CurrentFormatVersion;

            var manifestPath = Path.Combine(tempFolder, ProjectFileNames.Manifest);
            var json = JsonSerializer.Serialize(package.Document, SerializerOptions);
            await File.WriteAllTextAsync(manifestPath, json, cancellationToken);

            await SaveChannelAsync(tempFolder, ProjectFileNames.RedChannel, package.Red, cancellationToken);
            await SaveChannelAsync(tempFolder, ProjectFileNames.GreenChannel, package.Green, cancellationToken);
            await SaveChannelAsync(tempFolder, ProjectFileNames.BlueChannel, package.Blue, cancellationToken);
            await SaveResultAsync(tempFolder, ProjectFileNames.Result, package.Result, cancellationToken);
            await SaveProvenanceAsync(tempFolder, ProjectFileNames.RedProvenance, package.RedProvenance, cancellationToken);
            await SaveProvenanceAsync(tempFolder, ProjectFileNames.GreenProvenance, package.GreenProvenance, cancellationToken);
            await SaveProvenanceAsync(tempFolder, ProjectFileNames.BlueProvenance, package.BlueProvenance, cancellationToken);

            CommitTempFolder(folderPath, tempFolder);
        }
        catch
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }

            throw;
        }
    }

    private static void CommitTempFolder(string folderPath, string tempFolder)
    {
        foreach (var file in Directory.GetFiles(folderPath))
        {
            var name = Path.GetFileName(file);
            if (ProjectFileNames.IsProjectFile(name))
            {
                File.Delete(file);
            }
        }

        foreach (var file in Directory.GetFiles(tempFolder))
        {
            var destination = Path.Combine(folderPath, Path.GetFileName(file));
            File.Move(file, destination, overwrite: true);
        }

        Directory.Delete(tempFolder, recursive: true);
    }

    private static async Task SaveChannelAsync(
        string folder,
        string fileName,
        ImageBuffer? image,
        CancellationToken cancellationToken)
    {
        if (image is null)
        {
            return;
        }

        await ImageLoader.SaveGrayscaleTiffAsync(
            Path.Combine(folder, fileName),
            image,
            cancellationToken);
    }

    private static async Task SaveResultAsync(
        string folder,
        string fileName,
        RgbImageBuffer? result,
        CancellationToken cancellationToken)
    {
        if (result is null)
        {
            return;
        }

        var settings = RgbExportSettings.Default with
        {
            Format = RgbExportFormat.Tiff,
            TiffCompression = TiffExportCompression.Deflate,
            TiffDeflateLevel = 6,
            MaxSide = null,
        };

        await ImageLoader.SaveRgbAsync(Path.Combine(folder, fileName), result, settings, cancellationToken);
    }

    private static Task SaveProvenanceAsync(
        string folder,
        string fileName,
        RetouchProvenanceMap? provenance,
        CancellationToken cancellationToken) =>
        provenance is null
            ? Task.CompletedTask
            : File.WriteAllBytesAsync(Path.Combine(folder, fileName), provenance.ToArray(), cancellationToken);

    private static async Task<RetouchProvenanceMap?> LoadProvenanceAsync(
        string path,
        ImageBuffer? image,
        CancellationToken cancellationToken)
    {
        if (image is null)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            // Optional sidecars keep the v1 project format backward compatible.
            return RetouchProvenanceMap.Unknown(image.Width, image.Height);
        }

        var values = await File.ReadAllBytesAsync(path, cancellationToken);
        return values.Length == image.PixelCount
            ? new RetouchProvenanceMap(image.Width, image.Height, values)
            : RetouchProvenanceMap.Unknown(image.Width, image.Height);
    }

    private static async Task<RgbImageBuffer?> LoadResultAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        using var image = await SixLabors.ImageSharp.Image.LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgb24>(
            stream,
            cancellationToken);
        var pixels = new float[image.Width * image.Height * 3];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    var offset = ((y * image.Width) + x) * 3;
                    pixels[offset] = row[x].R / 255.0f;
                    pixels[offset + 1] = row[x].G / 255.0f;
                    pixels[offset + 2] = row[x].B / 255.0f;
                }
            }
        });

        return new RgbImageBuffer(image.Width, image.Height, pixels);
    }
}
