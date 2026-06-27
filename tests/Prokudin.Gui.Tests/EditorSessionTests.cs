using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Gui.Editing;

namespace Prokudin.Gui.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public void CreateMemento_ClonesChannelBuffers()
    {
        var red = ImageBuffer.Filled(4, 4, 0.2f);
        var memento = EditorSession.CreateMemento(new EditorCaptureState(
            Red: red,
            Green: null,
            Blue: null,
            RedSourcePath: "red.png",
            GreenSourcePath: null,
            BlueSourcePath: null,
            Result: null,
            LastAligned: null,
            RedExposureStops: 0,
            GreenExposureStops: 0,
            BlueExposureStops: 0,
            AutoWhiteBalance: true,
            WhiteBalancePipetteX: -1,
            WhiteBalancePipetteY: -1,
            LevelsMode: LevelsMode.Manual,
            LevelsBlackPoint: 0.1,
            LevelsWhitePoint: 0.9,
            LevelsGamma: 1.2,
            SelectedSlotDisplayName: "Red"));

        memento.Red.Should().NotBeSameAs(red);
        memento.Red![0, 0].Should().BeApproximately(0.2f, 1e-6f);
        memento.LevelsMode.Should().Be(LevelsMode.Manual);
        memento.LevelsBlackPoint.Should().Be(0.1);
        memento.LevelsGamma.Should().Be(1.2);
    }

    [Fact]
    public void CloneForRestore_ProducesIndependentCopy()
    {
        var memento = EditorSession.CreateMemento(new EditorCaptureState(
            Red: ImageBuffer.Filled(2, 2, 0.5f),
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
            WhiteBalancePipetteX: 1,
            WhiteBalancePipetteY: 2,
            LevelsMode: LevelsMode.AutoPercentile,
            LevelsBlackPoint: 0,
            LevelsWhitePoint: 1,
            LevelsGamma: 1,
            SelectedSlotDisplayName: "Green"));

        var restored = EditorSession.CloneForRestore(memento);
        restored.Should().NotBeSameAs(memento);
        restored.Red.Should().NotBeSameAs(memento.Red);
        restored.Red![0, 0].Should().BeApproximately(0.5f, 1e-6f);
    }

    [Fact]
    public void CreateMemento_ClonesAlignedChannels()
    {
        var mask = new byte[] { 1, 1, 1, 1 };
        var aligned = new AlignedChannels(
            ImageBuffer.Filled(2, 2, 0.1f),
            ImageBuffer.Filled(2, 2, 0.2f),
            ImageBuffer.Filled(2, 2, 0.3f),
            mask,
            mask,
            mask,
            new Dictionary<ChannelName, AlignChannelMetadata>());

        var memento = EditorSession.CreateMemento(new EditorCaptureState(
            Red: null,
            Green: null,
            Blue: null,
            RedSourcePath: null,
            GreenSourcePath: null,
            BlueSourcePath: null,
            Result: null,
            LastAligned: aligned,
            RedExposureStops: 0,
            GreenExposureStops: 0,
            BlueExposureStops: 0,
            AutoWhiteBalance: false,
            WhiteBalancePipetteX: -1,
            WhiteBalancePipetteY: -1,
            LevelsMode: LevelsMode.AutoPercentile,
            LevelsBlackPoint: 0,
            LevelsWhitePoint: 1,
            LevelsGamma: 1,
            SelectedSlotDisplayName: null));

        memento.LastAligned.Should().NotBeSameAs(aligned);
        memento.LastAligned!.Red.Should().NotBeSameAs(aligned.Red);
    }
}
