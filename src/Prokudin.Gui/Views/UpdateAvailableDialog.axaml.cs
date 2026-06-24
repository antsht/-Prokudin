using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Views;

public sealed partial class UpdateAvailableDialog : Window
{
    private string releaseUrl = string.Empty;

    public UpdateAvailableDialog()
    {
        InitializeComponent();
    }

    public UpdateAvailableDialog(UpdateCheckResult result)
        : this()
    {
        ApplyResult(result);
    }

    internal void ApplyResult(UpdateCheckResult result)
    {
        releaseUrl = result.ReleaseUrl ?? string.Empty;
        VersionTextBlock.Text = result.LatestVersion is null
            ? "Download the latest build from GitHub Releases."
            : $"Installed version is older than {result.LatestVersion}.";
        ReleaseNotesTextBlock.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
            ? "Release notes are available on the GitHub release page."
            : result.ReleaseNotes.Trim();
        OpenReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(releaseUrl);
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenRelease(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(releaseUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = releaseUrl,
            UseShellExecute = true,
        });
        Close();
    }
}
