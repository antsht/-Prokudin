using FluentAssertions;
using Prokudin.Gui.Services;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Tests;

public sealed class JsonUiSettingsStoreTests
{
    [Fact]
    public void RoundTrip_PreservesThemeAndPanelWidths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-settings-{Guid.NewGuid():N}.json");
        var store = new JsonUiSettingsStore(path);
        var settings = new UiSettings
        {
            ThemeMode = AppThemeMode.Dark,
            LeftPanelWidth = 300,
            RightInspectorWidth = 400,
            ProcessingLogHeight = 180,
            IsProcessingLogVisible = false,
            SelectedWorkflowTool = WorkflowTool.Clean,
        };
        store.Save(settings);
        store.Load().Should().BeEquivalentTo(settings);
        File.Delete(path);
    }

    [Fact]
    public void Normalize_ClampsPanelSizes()
    {
        var settings = new UiSettings
        {
            LeftPanelWidth = 100,
            RightInspectorWidth = 900,
            ProcessingLogHeight = 10,
        };

        settings.Normalize().Should().BeEquivalentTo(new UiSettings
        {
            LeftPanelWidth = 220,
            RightInspectorWidth = 520,
            ProcessingLogHeight = 44,
        }, options => options.ExcludingMissingMembers());
    }
}
