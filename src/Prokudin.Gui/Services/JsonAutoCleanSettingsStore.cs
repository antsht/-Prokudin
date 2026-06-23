using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prokudin.Gui.Services;

public sealed class JsonAutoCleanSettingsStore : IAutoCleanSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;

    public JsonAutoCleanSettingsStore()
        : this(DefaultPath())
    {
    }

    public JsonAutoCleanSettingsStore(string path)
    {
        this.path = path;
    }

    public AutoCleanSettingsSnapshot Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AutoCleanSettingsSnapshot();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AutoCleanSettingsSnapshot>(json, SerializerOptions)
                   ?? new AutoCleanSettingsSnapshot();
        }
        catch (JsonException)
        {
            return new AutoCleanSettingsSnapshot();
        }
        catch (IOException)
        {
            return new AutoCleanSettingsSnapshot();
        }
        catch (UnauthorizedAccessException)
        {
            return new AutoCleanSettingsSnapshot();
        }
    }

    public void Save(AutoCleanSettingsSnapshot settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Prokudin", "auto-clean-settings.json");
    }
}
