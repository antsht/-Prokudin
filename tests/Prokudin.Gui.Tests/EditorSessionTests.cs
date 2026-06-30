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
            ColorTemperature: 0,
            ColorTint: 0,
            SelectedSlotDisplayName: "Red"));

        memento.Kind.Should().Be(EditorMementoKind.Snapshot);
        memento.Red.Should().NotBeSameAs(red);
        memento.Red![0, 0].Should().BeApproximately(0.2f, 1e-6f);
        memento.LevelsMode.Should().Be(LevelsMode.Manual);
        memento.LevelsBlackPoint.Should().Be(0.1);
        memento.LevelsGamma.Should().Be(1.2);
        memento.ApproximateBytes.Should().Be(4 * 4 * sizeof(float));
    }

    [Fact]
    public void CreateParameterMemento_StoresScalarsOnly()
    {
        var red = ImageBuffer.Filled(4, 4, 0.2f);
        var result = new RgbImageBuffer(4, 4, Enumerable.Repeat(0.3f, 4 * 4 * 3).ToArray());
        var memento = EditorSession.CreateMemento(
            new EditorCaptureState(
                Red: red,
                Green: ImageBuffer.Filled(4, 4, 0.3f),
                Blue: ImageBuffer.Filled(4, 4, 0.4f),
                RedSourcePath: "red.png",
                GreenSourcePath: "green.png",
                BlueSourcePath: "blue.png",
                Result: result,
                LastAligned: null,
                RedExposureStops: 0.5,
                GreenExposureStops: 0.25,
                BlueExposureStops: -0.25,
                AutoWhiteBalance: true,
                WhiteBalancePipetteX: 1,
                WhiteBalancePipetteY: 2,
                LevelsMode: LevelsMode.Manual,
                LevelsBlackPoint: 0.1,
                LevelsWhitePoint: 0.9,
                LevelsGamma: 1.2,
                ColorTemperature: 100,
                ColorTint: -50,
                SelectedSlotDisplayName: "Result"),
            EditorMementoKind.Parameter);

        memento.Kind.Should().Be(EditorMementoKind.Parameter);
        memento.Red.Should().BeNull();
        memento.Green.Should().BeNull();
        memento.Blue.Should().BeNull();
        memento.RedSourcePath.Should().BeNull();
        memento.LastAligned.Should().BeNull();
        memento.SelectedSlotDisplayName.Should().BeNull();
        memento.RedExposureStops.Should().Be(0.5);
        memento.LevelsGamma.Should().Be(1.2);
        memento.ApproximateBytes.Should().Be(0);
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
            ColorTemperature: 0,
            ColorTint: 0,
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
            ColorTemperature: 0,
            ColorTint: 0,
            SelectedSlotDisplayName: null));

        memento.LastAligned.Should().NotBeSameAs(aligned);
        memento.LastAligned!.Red.Should().NotBeSameAs(aligned.Red);
    }
}
