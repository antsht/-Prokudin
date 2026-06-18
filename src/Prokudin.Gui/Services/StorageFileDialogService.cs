using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Prokudin.Core.Imaging;

namespace Prokudin.Gui.Services;

public sealed class StorageFileDialogService : IFileDialogService
{
    private static readonly FilePickerFileType ImageFiles = new(
        "Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff"],
        MimeTypes = ["image/png", "image/jpeg", "image/tiff"],
    };

    private static readonly FilePickerFileType PngFiles = new(
        "PNG")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };

    private static readonly FilePickerFileType JpegFiles = new(
        "JPEG")
    {
        Patterns = ["*.jpg", "*.jpeg"],
        MimeTypes = ["image/jpeg"],
    };

    private static readonly FilePickerFileType TiffFiles = new(
        "TIFF")
    {
        Patterns = ["*.tif", "*.tiff"],
        MimeTypes = ["image/tiff"],
    };

    private readonly Window owner;

    public StorageFileDialogService(Window owner)
    {
        this.owner = owner;
    }

    public async Task<string?> OpenImage()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open image",
                AllowMultiple = false,
                FileTypeFilter = [ImageFiles, FilePickerFileTypes.All],
            });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> OpenFolder()
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Export channels",
                AllowMultiple = false,
            });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    public async Task<string?> SaveExport(RgbExportSettings settings)
    {
        settings = settings.Normalize();
        var fileType = settings.Format switch
        {
            RgbExportFormat.Jpeg => JpegFiles,
            RgbExportFormat.Tiff => TiffFiles,
            _ => PngFiles,
        };
        var file = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export result",
                DefaultExtension = settings.DefaultExtension,
                SuggestedFileName = $"prokudin-result.{settings.DefaultExtension}",
                FileTypeChoices = [fileType, PngFiles, JpegFiles, TiffFiles],
            });

        return file?.Path.LocalPath;
    }
}
