using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.Editing;

public sealed record EditorCaptureState(
    ImageBuffer? Red,
    ImageBuffer? Green,
    ImageBuffer? Blue,
    string? RedSourcePath,
    string? GreenSourcePath,
    string? BlueSourcePath,
    RgbImageBuffer? Result,
    AlignedChannels? LastAligned,
    double RedExposureStops,
    double GreenExposureStops,
    double BlueExposureStops,
    bool AutoWhiteBalance,
    int WhiteBalancePipetteX,
    int WhiteBalancePipetteY,
    LevelsMode LevelsMode,
    double LevelsBlackPoint,
    double LevelsWhitePoint,
    double LevelsGamma,
    int ColorTemperature,
    int ColorTint,
    string? SelectedSlotDisplayName);
