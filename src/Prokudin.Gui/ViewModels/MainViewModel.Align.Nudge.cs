using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Transform;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    private const double ManualNudgeStepPx = 1.0;
    private const double ManualNudgeShiftStepPx = 5.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUncommittedManualNudge))]
    private double manualNudgeRedDx;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUncommittedManualNudge))]
    private double manualNudgeRedDy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUncommittedManualNudge))]
    private double manualNudgeBlueDx;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUncommittedManualNudge))]
    private double manualNudgeBlueDy;

    public bool HasUncommittedManualNudge =>
        Math.Abs(ManualNudgeRedDx) > 1e-6 ||
        Math.Abs(ManualNudgeRedDy) > 1e-6 ||
        Math.Abs(ManualNudgeBlueDx) > 1e-6 ||
        Math.Abs(ManualNudgeBlueDy) > 1e-6;

    public bool CanManualNudgeRed => AlignReference != ChannelName.Red;

    public bool CanManualNudgeBlue => AlignReference != ChannelName.Blue;

    [RelayCommand(CanExecute = nameof(CanCommitManualNudge))]
    private void CommitManualNudge()
    {
        if (lastAligned is null || !HasUncommittedManualNudge)
        {
            return;
        }

        var manual = CurrentManualNudges();
        var baked = ReconstructionPipeline.ApplyManualToAligned(lastAligned, manual);
        RecordSnapshotCommand("CommitManualNudge");
        SetLastAligned(baked);
        SetPreparedChannels(baked);
        ClearManualNudges(suppressRebuild: true);
        ResultSlot.Result = ReconstructionPipeline.BuildRgb(baked, CurrentPipelineSettings(skipCrop: true)).Rgb;
        RefreshPreviewBindings();
        RefreshChannelStates();
        Status = "Alignment nudge committed to prepared channels.";
        AppendLog("Manual alignment nudge committed.");
    }

    [RelayCommand]
    private void ResetManualNudge()
    {
        ClearManualNudges();
        Status = "Manual nudge reset.";
        AppendLog("Manual alignment nudge reset.");
    }

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeRedLeft() => AdjustManualNudge(ChannelName.Red, -ManualNudgeStepPx, 0);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeRedRight() => AdjustManualNudge(ChannelName.Red, ManualNudgeStepPx, 0);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeRedUp() => AdjustManualNudge(ChannelName.Red, 0, -ManualNudgeStepPx);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeRedDown() => AdjustManualNudge(ChannelName.Red, 0, ManualNudgeStepPx);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeBlueLeft() => AdjustManualNudge(ChannelName.Blue, -ManualNudgeStepPx, 0);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeBlueRight() => AdjustManualNudge(ChannelName.Blue, ManualNudgeStepPx, 0);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeBlueUp() => AdjustManualNudge(ChannelName.Blue, 0, -ManualNudgeStepPx);

    [RelayCommand(CanExecute = nameof(CanNudgeAlign))]
    private void NudgeBlueDown() => AdjustManualNudge(ChannelName.Blue, 0, ManualNudgeStepPx);

    private bool CanCommitManualNudge() => !IsBusy && lastAligned is not null && HasUncommittedManualNudge;

    private bool CanNudgeAlign() => !IsBusy && lastAligned is not null;

    partial void OnManualNudgeRedDxChanged(double value) => OnManualNudgeChanged();

    partial void OnManualNudgeRedDyChanged(double value) => OnManualNudgeChanged();

    partial void OnManualNudgeBlueDxChanged(double value) => OnManualNudgeChanged();

    partial void OnManualNudgeBlueDyChanged(double value) => OnManualNudgeChanged();

    private void OnManualNudgeChanged()
    {
        CommitManualNudgeCommand.NotifyCanExecuteChanged();
        ScheduleResultRebuild();
    }

    private void AdjustManualNudge(ChannelName channel, double deltaX, double deltaY)
    {
        if (channel == ChannelName.Red)
        {
            if (!CanManualNudgeRed)
            {
                return;
            }

            ManualNudgeRedDx = ClampNudge(ManualNudgeRedDx + deltaX);
            ManualNudgeRedDy = ClampNudge(ManualNudgeRedDy + deltaY);
            return;
        }

        if (channel == ChannelName.Blue)
        {
            if (!CanManualNudgeBlue)
            {
                return;
            }

            ManualNudgeBlueDx = ClampNudge(ManualNudgeBlueDx + deltaX);
            ManualNudgeBlueDy = ClampNudge(ManualNudgeBlueDy + deltaY);
        }
    }

    private static double ClampNudge(double value) => Math.Clamp(value, -20.0, 20.0);

    internal IReadOnlyDictionary<ChannelName, ManualTransform> CurrentManualNudges()
    {
        var nudges = new Dictionary<ChannelName, ManualTransform>();
        if (CanManualNudgeRed && (Math.Abs(ManualNudgeRedDx) > 1e-6f || Math.Abs(ManualNudgeRedDy) > 1e-6f))
        {
            nudges[ChannelName.Red] = new ManualTransform((float)ManualNudgeRedDx, (float)ManualNudgeRedDy);
        }

        if (CanManualNudgeBlue && (Math.Abs(ManualNudgeBlueDx) > 1e-6f || Math.Abs(ManualNudgeBlueDy) > 1e-6f))
        {
            nudges[ChannelName.Blue] = new ManualTransform((float)ManualNudgeBlueDx, (float)ManualNudgeBlueDy);
        }

        return nudges;
    }

    internal void ClearManualNudges(bool suppressRebuild = false)
    {
        suppressUndoCapture = true;
        try
        {
            ManualNudgeRedDx = 0;
            ManualNudgeRedDy = 0;
            ManualNudgeBlueDx = 0;
            ManualNudgeBlueDy = 0;
        }
        finally
        {
            suppressUndoCapture = false;
        }

        CommitManualNudgeCommand.NotifyCanExecuteChanged();
        NotifyManualNudgeCommands();
        if (!suppressRebuild)
        {
            ScheduleResultRebuild();
        }
    }

    private void NotifyManualNudgeCommands()
    {
        NudgeRedLeftCommand.NotifyCanExecuteChanged();
        NudgeRedRightCommand.NotifyCanExecuteChanged();
        NudgeRedUpCommand.NotifyCanExecuteChanged();
        NudgeRedDownCommand.NotifyCanExecuteChanged();
        NudgeBlueLeftCommand.NotifyCanExecuteChanged();
        NudgeBlueRightCommand.NotifyCanExecuteChanged();
        NudgeBlueUpCommand.NotifyCanExecuteChanged();
        NudgeBlueDownCommand.NotifyCanExecuteChanged();
        ResetManualNudgeCommand.NotifyCanExecuteChanged();
    }
}
