using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prokudin.Gui.Services;

public sealed class JsonUiSettingsStore : IUiSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;

    public JsonUiSettingsStore()
        : this(DefaultPath())
    {
    }

    public JsonUiSettingsStore(string path)
    {
        this.path = path;
    }

    public UiSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new UiSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiSettings>(json, SerializerOptions)?.Normalize()
                   ?? new UiSettings();
        }
        catch (JsonException)
        {
            return new UiSettings();
        }
        catch (IOException)
        {
            return new UiSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new UiSettings();
        }
    }

    public void Save(UiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var json = JsonSerializer.Serialize(settings.Normalize(), SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Prokudin", "ui-settings.json");
    }
}
