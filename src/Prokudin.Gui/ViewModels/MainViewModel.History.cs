using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Gui.Editing;
using Prokudin.Gui.Editing.Commands;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    private readonly EditorHistory editorHistory = new(EditorHistory.DefaultLimit);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!CanUndo())
        {
            return;
        }

        ClearPendingAutoCleanMask();
        CloseColorCoalesceWindow();
        PerformUndo();
        Status = "Undo.";
        AppendLog("Undo.");
        NotifyHistoryCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!CanRedo())
        {
            return;
        }

        ClearPendingAutoCleanMask();
        CloseColorCoalesceWindow();
        PerformRedo();
        Status = "Redo.";
        AppendLog("Redo.");
        NotifyHistoryCommands();
    }

    private EditorCaptureState BuildEditorCaptureState() =>
        new(
            Red: RedSlot.Image,
            Green: GreenSlot.Image,
            Blue: BlueSlot.Image,
            RedSourcePath: RedSlot.SourcePath,
            GreenSourcePath: GreenSlot.SourcePath,
            BlueSourcePath: BlueSlot.SourcePath,
            Result: ResultSlot.Result,
            LastAligned: lastAligned,
            RedExposureStops: RedExposureStops,
            GreenExposureStops: GreenExposureStops,
            BlueExposureStops: BlueExposureStops,
            AutoWhiteBalance: AutoWhiteBalance,
            WhiteBalancePipetteX: whiteBalancePipetteX,
            WhiteBalancePipetteY: whiteBalancePipetteY,
            LevelsMode: LevelsMode,
            LevelsBlackPoint: LevelsBlackPoint,
            LevelsWhitePoint: LevelsWhitePoint,
            LevelsGamma: LevelsGamma,
            ColorTemperature: ColorTemperature,
            ColorTint: ColorTint,
            SelectedSlotDisplayName: SelectedSlot?.DisplayName);

    private EditorMemento CaptureSnapshot() =>
        CaptureEditorMemento(EditorMementoKind.Snapshot);

    private EditorMemento CaptureEditorMemento(EditorMementoKind kind) =>
        EditorSession.CreateMemento(BuildEditorCaptureState(), kind);

    private void RecordSnapshotCommand(string operation)
    {
        if (isRestoringSnapshot || suppressUndoCapture)
        {
            return;
        }

        editorHistory.Record(new SnapshotCommand(CaptureSnapshot(), operation));
        MarkProjectDirty();
        NotifyHistoryCommands();
    }

    private void BeginCoalescedColorEdit()
    {
        if (isRestoringSnapshot || suppressUndoCapture)
        {
            return;
        }

        if (!colorCoalesceOpen)
        {
            editorHistory.Record(new CoalescedParameterCommand(
                CaptureEditorMemento(EditorMementoKind.Parameter),
                CoalescedParameterCommand.ColorAdjustKey,
                "ColorAdjust"));
            MarkProjectDirty();
            NotifyHistoryCommands();
            colorCoalesceOpen = true;
        }

        var version = ++colorChangeVersion;
        _ = Task.Run(async () =>
        {
            await Task.Delay(CoalescedParameterCommand.DefaultMergeWindow);
            if (version == colorChangeVersion)
            {
                colorCoalesceOpen = false;
            }
        });
    }

    private void CloseColorCoalesceWindow()
    {
        colorChangeVersion++;
        colorCoalesceOpen = false;
    }

    private void RestoreSnapshot(EditorMemento snapshot)
    {
        ClearPendingAutoCleanMask();
        isRestoringSnapshot = true;
        try
        {
            ApplyEditorMemento(EditorSession.CloneForRestore(snapshot));
        }
        finally
        {
            isRestoringSnapshot = false;
        }

        RefreshPreviewImageContext();
    }

    private void ApplyEditorMemento(EditorMemento snapshot)
    {
        if (snapshot.Kind == EditorMementoKind.Snapshot)
        {
            RedSlot.Image = snapshot.Red;
            GreenSlot.Image = snapshot.Green;
            BlueSlot.Image = snapshot.Blue;
            RedSlot.SourcePath = snapshot.RedSourcePath;
            GreenSlot.SourcePath = snapshot.GreenSourcePath;
            BlueSlot.SourcePath = snapshot.BlueSourcePath;
            SetLastAligned(snapshot.LastAligned);
        }

        RedExposureStops = snapshot.RedExposureStops;
        GreenExposureStops = snapshot.GreenExposureStops;
        BlueExposureStops = snapshot.BlueExposureStops;
        AutoWhiteBalance = snapshot.AutoWhiteBalance;
        whiteBalancePipetteX = snapshot.WhiteBalancePipetteX;
        whiteBalancePipetteY = snapshot.WhiteBalancePipetteY;
        LevelsMode = snapshot.LevelsMode;
        LevelsBlackPoint = snapshot.LevelsBlackPoint;
        LevelsWhitePoint = snapshot.LevelsWhitePoint;
        LevelsGamma = snapshot.LevelsGamma;
        ColorTemperature = snapshot.ColorTemperature;
        ColorTint = snapshot.ColorTint;

        if (snapshot.Kind == EditorMementoKind.Snapshot)
        {
            SelectedSlot = snapshot.SelectedSlotDisplayName switch
            {
                "Red" => RedSlot,
                "Green" => GreenSlot,
                "Blue" => BlueSlot,
                "Result" => ResultSlot,
                _ => RedSlot,
            };
        }

        ResultSlot.Result = BuildRestoredResult();
    }

    private RgbImageBuffer? BuildRestoredResult()
    {
        if (lastAligned is null)
        {
            return null;
        }

        var settings = CurrentPipelineSettings(skipCrop: true);
        var manual = CurrentManualNudges();
        return ReconstructionPipeline
            .BuildRgb(lastAligned, settings, manual.Count > 0 ? manual : null)
            .Rgb;
    }

    private bool CanUndo() => !IsBusy && editorHistory.CanUndo;

    private bool CanRedo() => !IsBusy && editorHistory.CanRedo;

    private void PerformUndo()
    {
        var targetKind = editorHistory.NextUndoKind;
        if (targetKind is null)
        {
            return;
        }

        var snapshot = editorHistory.TakeUndoTarget(CaptureEditorMemento(targetKind.Value));
        if (snapshot is null)
        {
            return;
        }

        RestoreSnapshot(snapshot);
    }

    private void PerformRedo()
    {
        var targetKind = editorHistory.NextRedoKind;
        if (targetKind is null)
        {
            return;
        }

        var snapshot = editorHistory.TakeRedoTarget(CaptureEditorMemento(targetKind.Value));
        if (snapshot is null)
        {
            return;
        }

        RestoreSnapshot(snapshot);
    }

    private void ClearEditorHistory()
    {
        editorHistory.Clear();
        NotifyHistoryCommands();
    }

    private void NotifyHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
