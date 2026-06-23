using System.Text.Json;

namespace Prokudin.Gui.Services;

public sealed class JsonProcessingDiagnosticsSettingsStore : IProcessingDiagnosticsSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string path;

    public JsonProcessingDiagnosticsSettingsStore()
        : this(DefaultPath())
    {
    }

    public JsonProcessingDiagnosticsSettingsStore(string path)
    {
        this.path = path;
    }

    public ProcessingDiagnosticsSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return ProcessingDiagnosticsSettings.Default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProcessingDiagnosticsSettings>(json, SerializerOptions)
                   ?? ProcessingDiagnosticsSettings.Default;
        }
        catch (JsonException)
        {
            return ProcessingDiagnosticsSettings.Default;
        }
        catch (IOException)
        {
            return ProcessingDiagnosticsSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return ProcessingDiagnosticsSettings.Default;
        }
    }

    public void Save(ProcessingDiagnosticsSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Prokudin", "diagnostics-settings.json");
    }
}
