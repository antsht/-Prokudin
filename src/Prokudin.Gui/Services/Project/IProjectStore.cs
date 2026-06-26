namespace Prokudin.Gui.Services.Project;

public interface IProjectStore
{
    bool IsValidProjectFolder(string folderPath);

    Task<ProjectPackage> LoadAsync(string folderPath, CancellationToken cancellationToken = default);

    Task SaveAsync(string folderPath, ProjectPackage package, CancellationToken cancellationToken = default);
}

public interface IAutosaveStore
{
    string FolderPath { get; }

    AutosaveInfo GetInfo();

    bool Exists();

    Task<ProjectPackage> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ProjectPackage package, CancellationToken cancellationToken = default);
}

public interface IRecentProjectsStore
{
    IReadOnlyList<RecentProjectEntry> Load();

    void RecordOpened(string folderPath, string? displayName);
}
