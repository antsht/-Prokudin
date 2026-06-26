using Prokudin.Core.Imaging;

namespace Prokudin.Gui.Services.Project;

public sealed class ProjectPackage
{
    public required ProjectDocument Document { get; init; }

    public ImageBuffer? Red { get; init; }

    public ImageBuffer? Green { get; init; }

    public ImageBuffer? Blue { get; init; }

    public RgbImageBuffer? Result { get; init; }
}

public sealed class AutosaveInfo
{
    public bool Exists { get; init; }

    public DateTimeOffset? SavedAt { get; init; }

    public string? LinkedProjectPath { get; init; }

    public string? DisplayName { get; init; }
}

public sealed class RecentProjectEntry
{
    public required string Path { get; init; }

    public string? DisplayName { get; init; }

    public DateTimeOffset LastOpenedAt { get; init; }
}
