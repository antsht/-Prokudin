using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.ViewModels;

public sealed partial class SettingsDialogViewModel : ObservableObject
{
    private readonly IUiSettingsStore uiSettingsStore;
    private readonly IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore;

    public SettingsDialogViewModel(
        IUiSettingsStore uiSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore)
    {
        this.uiSettingsStore = uiSettingsStore;
        this.diagnosticsSettingsStore = diagnosticsSettingsStore;

        var ui = uiSettingsStore.Load().Normalize();
        ThemeMode = ui.ThemeMode;
        AutosaveEnabled = ui.AutosaveEnabled;
        AutosaveIntervalMinutes = ui.AutosaveIntervalMinutes;

        var diagnostics = diagnosticsSettingsStore.Load();
        LogComputeBackends = diagnostics.LogComputeBackends;
        LogPipelineStages = diagnostics.LogPipelineStages;
        LogCpuParallel = diagnostics.LogCpuParallel;
        LogTimings = diagnostics.LogTimings;
    }

    [ObservableProperty]
    private AppThemeMode themeMode;

    [ObservableProperty]
    private bool autosaveEnabled = true;

    [ObservableProperty]
    private int autosaveIntervalMinutes = 10;

    [ObservableProperty]
    private bool logComputeBackends;

    [ObservableProperty]
    private bool logPipelineStages;

    [ObservableProperty]
    private bool logCpuParallel;

    [ObservableProperty]
    private bool logTimings;

    public IReadOnlyList<AppThemeMode> ThemeModes { get; } =
        [AppThemeMode.Light, AppThemeMode.Dark, AppThemeMode.System];

    public void Save()
    {
        var currentUi = uiSettingsStore.Load().Normalize();
        uiSettingsStore.Save(new UiSettings
        {
            ThemeMode = ThemeMode,
            LeftPanelWidth = currentUi.LeftPanelWidth,
            RightInspectorWidth = currentUi.RightInspectorWidth,
            ProcessingLogHeight = currentUi.ProcessingLogHeight,
            IsProcessingLogVisible = currentUi.IsProcessingLogVisible,
            IsRightInspectorVisible = currentUi.IsRightInspectorVisible,
            IsLeftPanelVisible = currentUi.IsLeftPanelVisible,
            SelectedWorkflowTool = currentUi.SelectedWorkflowTool,
            AutosaveEnabled = AutosaveEnabled,
            AutosaveIntervalMinutes = AutosaveIntervalMinutes,
        }.Normalize());

        diagnosticsSettingsStore.Save(new ProcessingDiagnosticsSettings(
            LogComputeBackends,
            LogPipelineStages,
            LogCpuParallel,
            LogTimings));
    }

    [RelayCommand]
    private void Apply(Window? owner)
    {
        Save();
        ThemeService.Apply(ThemeMode);
        owner?.Close();
    }
}
