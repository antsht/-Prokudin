using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.Editing;

public static class EditorSession
{
    public static EditorMemento CreateMemento(in EditorCaptureState state) =>
        CreateMemento(state, EditorMementoKind.Snapshot);

    public static EditorMemento CreateMemento(in EditorCaptureState state, EditorMementoKind kind) =>
        kind switch
        {
            EditorMementoKind.Parameter => CreateParameterMemento(state),
            EditorMementoKind.Snapshot => CreateSnapshotMemento(state),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported editor memento kind."),
        };

    private static EditorMemento CreateParameterMemento(in EditorCaptureState state) =>
        new(
            EditorMementoKind.Parameter,
            Red: null,
            Green: null,
            Blue: null,
            RedSourcePath: null,
            GreenSourcePath: null,
            BlueSourcePath: null,
            LastAligned: null,
            state.RedExposureStops,
            state.GreenExposureStops,
            state.BlueExposureStops,
            state.AutoWhiteBalance,
            state.WhiteBalancePipetteX,
            state.WhiteBalancePipetteY,
            state.LevelsMode,
            state.LevelsBlackPoint,
            state.LevelsWhitePoint,
            state.LevelsGamma,
            state.ColorTemperature,
            state.ColorTint,
            SelectedSlotDisplayName: null,
            state.WhiteBalanceSource, state.WhitePickRadius, state.WhitePickWarningAcknowledged,
            state.RedLevelsBlackPoint, state.RedLevelsWhitePoint, state.RedLevelsGamma,
            state.GreenLevelsBlackPoint, state.GreenLevelsWhitePoint, state.GreenLevelsGamma,
            state.BlueLevelsBlackPoint, state.BlueLevelsWhitePoint, state.BlueLevelsGamma);

    private static EditorMemento CreateSnapshotMemento(in EditorCaptureState state) =>
        new(
            EditorMementoKind.Snapshot,
            state.Red?.Clone(),
            state.Green?.Clone(),
            state.Blue?.Clone(),
            state.RedSourcePath,
            state.GreenSourcePath,
            state.BlueSourcePath,
            CloneAligned(state.LastAligned),
            state.RedExposureStops,
            state.GreenExposureStops,
            state.BlueExposureStops,
            state.AutoWhiteBalance,
            state.WhiteBalancePipetteX,
            state.WhiteBalancePipetteY,
            state.LevelsMode,
            state.LevelsBlackPoint,
            state.LevelsWhitePoint,
            state.LevelsGamma,
            state.ColorTemperature,
            state.ColorTint,
            state.SelectedSlotDisplayName,
            state.WhiteBalanceSource, state.WhitePickRadius, state.WhitePickWarningAcknowledged,
            state.RedLevelsBlackPoint, state.RedLevelsWhitePoint, state.RedLevelsGamma,
            state.GreenLevelsBlackPoint, state.GreenLevelsWhitePoint, state.GreenLevelsGamma,
            state.BlueLevelsBlackPoint, state.BlueLevelsWhitePoint, state.BlueLevelsGamma);

    public static EditorMemento CloneForRestore(in EditorMemento memento) =>
        new(
            memento.Kind,
            memento.Red?.Clone(),
            memento.Green?.Clone(),
            memento.Blue?.Clone(),
            memento.RedSourcePath,
            memento.GreenSourcePath,
            memento.BlueSourcePath,
            CloneAligned(memento.LastAligned),
            memento.RedExposureStops,
            memento.GreenExposureStops,
            memento.BlueExposureStops,
            memento.AutoWhiteBalance,
            memento.WhiteBalancePipetteX,
            memento.WhiteBalancePipetteY,
            memento.LevelsMode,
            memento.LevelsBlackPoint,
            memento.LevelsWhitePoint,
            memento.LevelsGamma,
            memento.ColorTemperature,
            memento.ColorTint,
            memento.SelectedSlotDisplayName,
            memento.WhiteBalanceSource, memento.WhitePickRadius, memento.WhitePickWarningAcknowledged,
            memento.RedLevelsBlackPoint, memento.RedLevelsWhitePoint, memento.RedLevelsGamma,
            memento.GreenLevelsBlackPoint, memento.GreenLevelsWhitePoint, memento.GreenLevelsGamma,
            memento.BlueLevelsBlackPoint, memento.BlueLevelsWhitePoint, memento.BlueLevelsGamma);

    internal static AlignedChannels? CloneAligned(AlignedChannels? aligned)
    {
        if (aligned is null)
        {
            return null;
        }

        return new AlignedChannels(
            aligned.Red.Clone(),
            aligned.Green.Clone(),
            aligned.Blue.Clone(),
            (byte[])aligned.MaskRed.Clone(),
            (byte[])aligned.MaskGreen.Clone(),
            (byte[])aligned.MaskBlue.Clone(),
            aligned.AlignMetadata,
            CloneTransforms(aligned.AlignTransforms));
    }

    private static IReadOnlyDictionary<ChannelName, ChannelAlignmentTransform>? CloneTransforms(
        IReadOnlyDictionary<ChannelName, ChannelAlignmentTransform>? transforms)
    {
        if (transforms is null)
        {
            return null;
        }

        return transforms.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Matrix = (double[])pair.Value.Matrix.Clone(),
                Shifts = pair.Value.Shifts.ToArray(),
            });
    }
}
