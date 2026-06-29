using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Editing;
using Prokudin.Gui.Editing.Commands;

namespace Prokudin.Gui.Tests;

public sealed class EditorHistoryTests
{
    [Fact]
    public void RecordSnapshot_ClearsRedoStack()
    {
        var history = new EditorHistory(limit: 4);
        var first = CreateMemento(0.1f);
        var second = CreateMemento(0.2f);
        var third = CreateMemento(0.3f);

        history.RecordSnapshot(first);
        _ = history.TakeUndoTarget(second);
        history.CanRedo.Should().BeTrue();

        history.RecordSnapshot(third);
        history.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void RecordSnapshot_TrimsToLimit()
    {
        var history = new EditorHistory(limit: 2);
        history.RecordSnapshot(CreateMemento(0.1f));
        history.RecordSnapshot(CreateMemento(0.2f));
        history.RecordSnapshot(CreateMemento(0.3f));

        history.CanUndo.Should().BeTrue();
        var restored = history.TakeUndoTarget(CreateMemento(0.4f));
        restored!.Red![0, 0].Should().BeApproximately(0.3f, 1e-6f);

        restored = history.TakeUndoTarget(CreateMemento(0.4f));
        restored!.Red![0, 0].Should().BeApproximately(0.2f, 1e-6f);

        history.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void TakeUndoAndRedo_RoundTripState()
    {
        var history = new EditorHistory();
        var before = CreateMemento(0.2f);
        var after = CreateMemento(0.8f);

        history.Record(new SnapshotCommand(before, "RetouchBrush"));
        var undoTarget = history.TakeUndoTarget(after);
        undoTarget.Should().BeSameAs(before);
        history.CanRedo.Should().BeTrue();

        var redoTarget = history.TakeRedoTarget(before);
        redoTarget.Should().BeSameAs(after);
        history.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void CoalescedParameterCommand_TryMergeWith_MatchesSameKey()
    {
        var memento = CreateMemento(0.2f);
        var first = new CoalescedParameterCommand(memento, CoalescedParameterCommand.ColorAdjustKey, "ColorAdjust");
        var second = new CoalescedParameterCommand(CreateMemento(0.3f), CoalescedParameterCommand.ColorAdjustKey, "ColorAdjust");
        var other = new CoalescedParameterCommand(memento, "Other", "Other");

        first.TryMergeWith(second).Should().BeTrue();
        first.TryMergeWith(other).Should().BeFalse();
    }

    private static EditorMemento CreateMemento(float value) =>
        EditorSession.CreateMemento(new EditorCaptureState(
            Red: ImageBuffer.Filled(2, 2, value),
            Green: null,
            Blue: null,
            RedSourcePath: null,
            GreenSourcePath: null,
            BlueSourcePath: null,
            Result: null,
            LastAligned: null,
            RedExposureStops: 0,
            GreenExposureStops: 0,
            BlueExposureStops: 0,
            AutoWhiteBalance: false,
            WhiteBalancePipetteX: -1,
            WhiteBalancePipetteY: -1,
            LevelsMode: Prokudin.Core.Color.LevelsMode.AutoPercentile,
            LevelsBlackPoint: 0,
            LevelsWhitePoint: 1,
            LevelsGamma: 1,
            ColorTemperature: 0,
            ColorTint: 0,
            SelectedSlotDisplayName: null));
}
