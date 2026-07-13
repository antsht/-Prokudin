using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Color;
using Prokudin.Core.Retouch;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    public double ActiveLevelsBlackPoint
    {
        get => LevelsScope switch
        {
            LevelsScope.Master => LevelsBlackPoint,
            LevelsScope.Red => RedLevelsBlackPoint,
            LevelsScope.Green => GreenLevelsBlackPoint,
            _ => BlueLevelsBlackPoint,
        };
        set => SetActiveLevels(blackPoint: value);
    }

    public double ActiveLevelsWhitePoint
    {
        get => LevelsScope switch
        {
            LevelsScope.Master => LevelsWhitePoint,
            LevelsScope.Red => RedLevelsWhitePoint,
            LevelsScope.Green => GreenLevelsWhitePoint,
            _ => BlueLevelsWhitePoint,
        };
        set => SetActiveLevels(whitePoint: value);
    }

    public double ActiveLevelsGamma
    {
        get => LevelsScope switch
        {
            LevelsScope.Master => LevelsGamma,
            LevelsScope.Red => RedLevelsGamma,
            LevelsScope.Green => GreenLevelsGamma,
            _ => BlueLevelsGamma,
        };
        set => SetActiveLevels(gamma: value);
    }

    [RelayCommand]
    private void ResetExposure()
    {
        RecordSnapshotCommand("ResetExposure");
        suppressUndoCapture = true;
        try
        {
            RedExposureStops = 0;
            GreenExposureStops = 0;
            BlueExposureStops = 0;
        }
        finally { suppressUndoCapture = false; }

        ScheduleResultRebuild();
        AppendLog("Exposure reset.");
    }

    [RelayCommand]
    private void ResetLevels()
    {
        RecordSnapshotCommand("ResetLevels");
        suppressUndoCapture = true;
        try
        {
            if (IsMasterLevelsScope)
            {
                LevelsMode = LevelsMode.AutoPercentile;
                LevelsBlackPoint = 0;
                LevelsWhitePoint = 1;
                LevelsGamma = 1;
            }
            else
            {
                SetActiveLevels(0, 1, 1);
            }
        }
        finally { suppressUndoCapture = false; }

        ScheduleResultRebuild();
        AppendLog("Levels reset.");
    }

    [RelayCommand]
    private void FullColorReset()
    {
        RecordSnapshotCommand("FullColorReset");
        suppressUndoCapture = true;
        try
        {
            RedExposureStops = 0;
            GreenExposureStops = 0;
            BlueExposureStops = 0;
            WhiteBalanceSource = WhiteBalanceSource.Auto;
            whiteBalancePipetteX = -1;
            whiteBalancePipetteY = -1;
            WhitePickRadius = DefaultWhitePickRadius;
            WhitePickWarningAcknowledged = false;
            ColorTemperature = 0;
            ColorTint = 0;
            LevelsScope = LevelsScope.Master;
            LevelsMode = LevelsMode.AutoPercentile;
            LevelsBlackPoint = 0;
            LevelsWhitePoint = 1;
            LevelsGamma = 1;
            RedLevelsBlackPoint = GreenLevelsBlackPoint = BlueLevelsBlackPoint = 0;
            RedLevelsWhitePoint = GreenLevelsWhitePoint = BlueLevelsWhitePoint = 1;
            RedLevelsGamma = GreenLevelsGamma = BlueLevelsGamma = 1;
        }
        finally { suppressUndoCapture = false; }

        OnPropertyChanged(nameof(WhitePickX));
        OnPropertyChanged(nameof(WhitePickY));
        OnPropertyChanged(nameof(ShowWhitePick));
        ScheduleResultRebuild();
        Status = "Full colour reset.";
        AppendLog(Status);
    }

    [RelayCommand(CanExecute = nameof(CanPickWhiteBalance))]
    private void PickWhiteBalance(RetouchPoint point)
    {
        if (ResultSlot.Result is not { } result) return;
        BeginCoalescedColorEdit();
        whiteBalancePipetteX = Math.Clamp((int)MathF.Round(point.X), 0, result.Width - 1);
        whiteBalancePipetteY = Math.Clamp((int)MathF.Round(point.Y), 0, result.Height - 1);
        WhitePickWarningAcknowledged = false;
        WhiteBalanceSource = WhiteBalanceSource.WhitePick;
        ToolMode = EditorToolMode.Select;
        OnPropertyChanged(nameof(WhitePickQualityWarning));
        OnPropertyChanged(nameof(WhitePickX));
        OnPropertyChanged(nameof(WhitePickY));
        OnPropertyChanged(nameof(ShowWhitePick));
        Status = $"White Pick set at {whiteBalancePipetteX}, {whiteBalancePipetteY}.";
        AppendLog($"White Pick: {whiteBalancePipetteX},{whiteBalancePipetteY}, radius {WhitePickRadius}.");
    }

    [RelayCommand]
    private void UseWhitePickAnyway()
    {
        BeginCoalescedColorEdit();
        WhitePickWarningAcknowledged = true;
        OnPropertyChanged(nameof(WhitePickQualityWarning));
    }

    public string? WhitePickQualityWarning =>
        !WhitePickWarningAcknowledged && WhiteBalanceSource == WhiteBalanceSource.WhitePick && ResultSlot.Result is { } result && HasPipetteWhiteBalance
            ? ColorCorrection.EvaluateWhitePick(result, new WhitePick(whiteBalancePipetteX, whiteBalancePipetteY, WhitePickRadius)).WarningMessage
            : null;

    public bool HasWhitePickQualityWarning => !string.IsNullOrWhiteSpace(WhitePickQualityWarning);

    private bool CanPickWhiteBalance(RetouchPoint point) => CanUseWhiteBalancePicker;

    partial void OnWhiteBalanceSourceChanging(WhiteBalanceSource oldValue, WhiteBalanceSource newValue) => BeginCoalescedColorEdit();

    partial void OnWhiteBalanceSourceChanged(WhiteBalanceSource value)
    {
        if (value == WhiteBalanceSource.WhitePick && CanUseWhiteBalancePicker)
        {
            SelectedSlot = ResultSlot;
            ToolMode = EditorToolMode.WhiteBalancePicker;
            Status = "Select a neutral area for White Pick.";
        }
        OnPropertyChanged(nameof(WhitePickQualityWarning));
        OnPropertyChanged(nameof(HasWhitePickQualityWarning));
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnWhitePickRadiusChanging(int oldValue, int newValue) => BeginCoalescedColorEdit();
    partial void OnWhitePickRadiusChanged(int value)
    {
        if (value is < 1 or > 25) { WhitePickRadius = Math.Clamp(value, 1, 25); return; }
        WhitePickWarningAcknowledged = false;
        OnPropertyChanged(nameof(WhitePickQualityWarning));
        OnPropertyChanged(nameof(HasWhitePickQualityWarning));
        ScheduleResultRebuild();
        MarkProjectDirty();
    }

    partial void OnWhitePickWarningAcknowledgedChanged(bool value) => MarkProjectDirty();
    partial void OnLevelsScopeChanged(LevelsScope value)
    {
        OnPropertyChanged(nameof(IsMasterLevelsScope));
        OnPropertyChanged(nameof(IsChannelLevelsScope));
        OnPropertyChanged(nameof(CanEditActiveLevels));
        OnPropertyChanged(nameof(ActiveLevelsMode));
        OnPropertyChanged(nameof(ActiveLevelsBlackPoint));
        OnPropertyChanged(nameof(ActiveLevelsWhitePoint));
        OnPropertyChanged(nameof(ActiveLevelsGamma));
    }

    partial void OnLevelsModeChanging(LevelsMode oldValue, LevelsMode newValue) => BeginCoalescedColorEdit();
    partial void OnLevelsModeChanged(LevelsMode value) { OnPropertyChanged(nameof(IsManualLevels)); OnPropertyChanged(nameof(CanEditActiveLevels)); ScheduleColorRebuild(); }
    partial void OnLevelsBlackPointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnLevelsWhitePointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnLevelsGammaChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnLevelsBlackPointChanged(double value) => ScheduleColorRebuild();
    partial void OnLevelsWhitePointChanged(double value) => ScheduleColorRebuild();
    partial void OnLevelsGammaChanged(double value) => ScheduleColorRebuild();

    partial void OnRedLevelsBlackPointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnRedLevelsWhitePointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnRedLevelsGammaChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnGreenLevelsBlackPointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnGreenLevelsWhitePointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnGreenLevelsGammaChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnBlueLevelsBlackPointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnBlueLevelsWhitePointChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnBlueLevelsGammaChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnRedLevelsBlackPointChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnRedLevelsWhitePointChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnRedLevelsGammaChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnGreenLevelsBlackPointChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnGreenLevelsWhitePointChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnGreenLevelsGammaChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnBlueLevelsBlackPointChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnBlueLevelsWhitePointChanged(double value) => ScheduleChannelLevelsRebuild();
    partial void OnBlueLevelsGammaChanged(double value) => ScheduleChannelLevelsRebuild();

    partial void OnRedExposureStopsChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnGreenExposureStopsChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnBlueExposureStopsChanging(double oldValue, double newValue) => BeginCoalescedColorEdit();
    partial void OnRedExposureStopsChanged(double value) => ScheduleColorRebuild();
    partial void OnGreenExposureStopsChanged(double value) => ScheduleColorRebuild();
    partial void OnBlueExposureStopsChanged(double value) => ScheduleColorRebuild();
    partial void OnColorTemperatureChanging(int oldValue, int newValue) => BeginCoalescedColorEdit();
    partial void OnColorTintChanging(int oldValue, int newValue) => BeginCoalescedColorEdit();
    partial void OnColorTemperatureChanged(int value) => ScheduleColorRebuild();
    partial void OnColorTintChanged(int value) => ScheduleColorRebuild();
    partial void OnIsLoupeEnabledChanged(bool value) { OnPropertyChanged(nameof(PreviewLoupeEnabled)); SaveUiSettings(); }

    private void SetActiveLevels(double? blackPoint = null, double? whitePoint = null, double? gamma = null)
    {
        switch (LevelsScope)
        {
            case LevelsScope.Master:
                if (blackPoint is { } masterBlack) LevelsBlackPoint = masterBlack;
                if (whitePoint is { } masterWhite) LevelsWhitePoint = masterWhite;
                if (gamma is { } masterGamma) LevelsGamma = masterGamma;
                break;
            case LevelsScope.Red:
                if (blackPoint is { } redBlack) RedLevelsBlackPoint = redBlack;
                if (whitePoint is { } redWhite) RedLevelsWhitePoint = redWhite;
                if (gamma is { } redGamma) RedLevelsGamma = redGamma;
                break;
            case LevelsScope.Green:
                if (blackPoint is { } greenBlack) GreenLevelsBlackPoint = greenBlack;
                if (whitePoint is { } greenWhite) GreenLevelsWhitePoint = greenWhite;
                if (gamma is { } greenGamma) GreenLevelsGamma = greenGamma;
                break;
            default:
                if (blackPoint is { } blueBlack) BlueLevelsBlackPoint = blueBlack;
                if (whitePoint is { } blueWhite) BlueLevelsWhitePoint = blueWhite;
                if (gamma is { } blueGamma) BlueLevelsGamma = blueGamma;
                break;
        }
    }

    private void ScheduleColorRebuild() { ScheduleResultRebuild(); MarkProjectDirty(); }
    private void ScheduleChannelLevelsRebuild()
    {
        OnPropertyChanged(nameof(ActiveLevelsBlackPoint));
        OnPropertyChanged(nameof(ActiveLevelsWhitePoint));
        OnPropertyChanged(nameof(ActiveLevelsGamma));
        ScheduleColorRebuild();
    }
}
