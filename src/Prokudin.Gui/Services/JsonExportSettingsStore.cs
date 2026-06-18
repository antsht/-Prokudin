using System.Text.Json;
using System.Text.Json.Serialization;
using Prokudin.Core.Imaging;

namespace Prokudin.Gui.Services;

public sealed class JsonExportSettingsStore : IExportSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;

    public JsonExportSettingsStore()
        : this(DefaultPath())
    {
    }

    public JsonExportSettingsStore(string path)
    {
        this.path = path;
    }

    public RgbExportSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return RgbExportSettings.Default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RgbExportSettings>(json, SerializerOptions)?.Normalize()
                   ?? RgbExportSettings.Default;
        }
        catch (JsonException)
        {
            return RgbExportSettings.Default;
        }
        catch (IOException)
        {
            return RgbExportSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return RgbExportSettings.Default;
        }
    }

    public void Save(RgbExportSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var json = JsonSerializer.Serialize(settings.Normalize(), SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Prokudin", "export-settings.json");
    }
}
