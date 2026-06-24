namespace Prokudin.Gui.Services;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(Version currentVersion, CancellationToken cancellationToken = default);
}
