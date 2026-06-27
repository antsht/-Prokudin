using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.Editing;

public static class EditorSession
{
    public static EditorMemento CreateMemento(in EditorCaptureState state) =>
        new(
            state.Red?.Clone(),
            state.Green?.Clone(),
            state.Blue?.Clone(),
            state.RedSourcePath,
            state.GreenSourcePath,
            state.BlueSourcePath,
            state.Result?.Clone(),
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
            state.SelectedSlotDisplayName);

    public static EditorMemento CloneForRestore(in EditorMemento memento) =>
        new(
            memento.Red?.Clone(),
            memento.Green?.Clone(),
            memento.Blue?.Clone(),
            memento.RedSourcePath,
            memento.GreenSourcePath,
            memento.BlueSourcePath,
            memento.Result?.Clone(),
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
            memento.SelectedSlotDisplayName);

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
