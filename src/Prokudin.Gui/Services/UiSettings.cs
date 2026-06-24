using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Services;

public sealed class UiSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    public double LeftPanelWidth { get; set; } = 260;

    public double RightInspectorWidth { get; set; } = 360;

    public double ProcessingLogHeight { get; set; } = 150;

    public bool IsProcessingLogVisible { get; set; } = true;

    public bool IsRightInspectorVisible { get; set; } = true;

    public bool IsLeftPanelVisible { get; set; } = true;

    public WorkflowTool SelectedWorkflowTool { get; set; } = WorkflowTool.Import;

    public UiSettings Normalize() =>
        new()
        {
            ThemeMode = ThemeMode,
            LeftPanelWidth = Math.Clamp(LeftPanelWidth, 220, 420),
            RightInspectorWidth = Math.Clamp(RightInspectorWidth, 300, 520),
            ProcessingLogHeight = Math.Clamp(ProcessingLogHeight, 44, 360),
            IsProcessingLogVisible = IsProcessingLogVisible,
            IsRightInspectorVisible = IsRightInspectorVisible,
            IsLeftPanelVisible = IsLeftPanelVisible,
            SelectedWorkflowTool = SelectedWorkflowTool,
        };
}
