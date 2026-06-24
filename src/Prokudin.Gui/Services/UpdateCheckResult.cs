namespace Prokudin.Gui.Services;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version? LatestVersion,
    string? ReleaseUrl,
    string? ReleaseNotes,
    string? ErrorMessage);
