using System.Text.Json;

namespace Prokudin.Gui.Services.Project;

public sealed class JsonRecentProjectsStore : IRecentProjectsStore
{
    private const int MaxEntries = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string path;

    public JsonRecentProjectsStore()
        : this(DefaultPath())
    {
    }

    public JsonRecentProjectsStore(string path)
    {
        this.path = path;
    }

    public IReadOnlyList<RecentProjectEntry> Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, SerializerOptions) ?? [];
            return entries
                .Where(entry => Directory.Exists(entry.Path) && File.Exists(Path.Combine(entry.Path, ProjectFileNames.Manifest)))
                .Take(MaxEntries)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void RecordOpened(string folderPath, string? displayName)
    {
        var normalizedPath = Path.GetFullPath(folderPath);
        var entries = Load()
            .Where(entry => !string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(new RecentProjectEntry
            {
                Path = normalizedPath,
                DisplayName = displayName,
                LastOpenedAt = DateTimeOffset.UtcNow,
            })
            .Take(MaxEntries)
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Prokudin", "recent-projects.json");
    }
}
