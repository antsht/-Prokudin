using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Color;
using Prokudin.Core.Retouch;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand]
    private void ResetExposure()
    {
        RecordSnapshotCommand("ResetExposure");
        suppressUndoCapture = true;
        try
        {
            RedExposureStops = 0.0;
            GreenExposureStops = 0.0;
            BlueExposureStops = 0.0;
        }
        finally
        {
            suppressUndoCapture = false;
        }

        ScheduleResultRebuild();
        AppendLog("Exposure reset.");
    }

    [RelayCommand(CanExecute = nameof(CanPickWhiteBalance))]
    private void PickWhiteBalance(RetouchPoint point)
    {
        if (ResultSlot.Result is not { } result)
        {
            return;
        }

        var x = Math.Clamp((int)MathF.Round(point.X), 0, result.Width - 1);
        var y = Math.Clamp((int)MathF.Round(point.Y), 0, result.Height - 1);

        BeginCoalescedColorEdit();
        whiteBalancePipetteX = x;
        whiteBalancePipetteY = y;
        if (AutoWhiteBalance)
        {
            AutoWhiteBalance = false;
        }
        else
        {
            ScheduleResultRebuild();
        }

        Status = $"White balance picked at {x}, {y}.";
        AppendLog($"White balance pipette: {x},{y}, radius {WhiteBalancePipetteRadius}.");
    }

    private bool CanPickWhiteBalance(RetouchPoint point)
    {
        return CanUseWhiteBalancePicker;
    }

    partial void OnLevelsModeChanging(LevelsMode oldValue, LevelsMode newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnLevelsModeChanged(LevelsMode value)
    {
        OnPropertyChanged(nameof(IsManualLevels));
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnLevelsBlackPointChanging(double oldValue, double newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnLevelsBlackPointChanged(double value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnLevelsWhitePointChanging(double oldValue, double newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnLevelsWhitePointChanged(double value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnLevelsGammaChanging(double oldValue, double newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnLevelsGammaChanged(double value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnAutoWhiteBalanceChanging(bool oldValue, bool newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnAutoWhiteBalanceChanged(bool value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnRedExposureStopsChanging(double oldValue, double newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnGreenExposureStopsChanging(double oldValue, double newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnBlueExposureStopsChanging(double oldValue, double newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnRedExposureStopsChanged(double value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnGreenExposureStopsChanged(double value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnBlueExposureStopsChanged(double value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnColorTemperatureChanging(int oldValue, int newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnColorTintChanging(int oldValue, int newValue)
    {
        BeginCoalescedColorEdit();
    }

    partial void OnColorTemperatureChanged(int value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnColorTintChanged(int value)
    {
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnIsLoupeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewLoupeEnabled));
        SaveUiSettings();
    }
}
