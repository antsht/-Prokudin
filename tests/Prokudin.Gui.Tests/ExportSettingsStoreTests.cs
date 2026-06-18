using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Tests;

public sealed class ExportSettingsStoreTests
{
    [Fact]
    public void Load_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-settings-{Guid.NewGuid():N}.json");
        var store = new JsonExportSettingsStore(path);

        var settings = store.Load();

        settings.Should().Be(RgbExportSettings.Default);
    }

    [Fact]
    public void Save_ThenLoad_RestoresSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonExportSettingsStore(path);
            var settings = RgbExportSettings.Default with
            {
                Format = RgbExportFormat.Tiff,
                MaxSide = 2400,
                PngCompression = 9,
                JpegQuality = 82,
                TiffCompression = TiffExportCompression.Deflate,
                TiffDeflateLevel = 7,
            };

            store.Save(settings);

            store.Load().Should().Be(settings);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{not valid json");
            var store = new JsonExportSettingsStore(path);

            var settings = store.Load();

            settings.Should().Be(RgbExportSettings.Default);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
