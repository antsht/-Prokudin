using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prokudin.Gui.Services.Project;

public sealed class JsonAutosaveStore : IAutosaveStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonProjectStore projectStore = new();

    public JsonAutosaveStore()
        : this(DefaultPath())
    {
    }

    public JsonAutosaveStore(string folderPath)
    {
        FolderPath = folderPath;
    }

    public string FolderPath { get; }

    public AutosaveInfo GetInfo()
    {
        var manifestPath = Path.Combine(FolderPath, ProjectFileNames.Manifest);
        if (!File.Exists(manifestPath))
        {
            return new AutosaveInfo { Exists = false };
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var document = JsonSerializer.Deserialize<ProjectDocument>(json, SerializerOptions);
            if (document is null)
            {
                return new AutosaveInfo { Exists = false };
            }

            return new AutosaveInfo
            {
                Exists = true,
                SavedAt = document.SavedAt,
                LinkedProjectPath = document.LinkedProjectPath,
                DisplayName = document.DisplayName,
            };
        }
        catch
        {
            return new AutosaveInfo { Exists = false };
        }
    }

    public bool Exists() => File.Exists(Path.Combine(FolderPath, ProjectFileNames.Manifest));

    public Task<ProjectPackage> LoadAsync(CancellationToken cancellationToken = default) =>
        projectStore.LoadAsync(FolderPath, cancellationToken);

    public Task SaveAsync(ProjectPackage package, CancellationToken cancellationToken = default) =>
        JsonProjectStore.SaveCoreAsync(FolderPath, package, cancellationToken);

    private static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Prokudin", "autosave");
    }
}
