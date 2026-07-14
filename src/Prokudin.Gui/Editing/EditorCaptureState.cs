using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Retouch;

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
    string? SelectedSlotDisplayName,
    WhiteBalanceSource WhiteBalanceSource = WhiteBalanceSource.Auto,
    int WhitePickRadius = 3,
    bool WhitePickWarningAcknowledged = false,
    double RedLevelsBlackPoint = 0,
    double RedLevelsWhitePoint = 1,
    double RedLevelsGamma = 1,
    double GreenLevelsBlackPoint = 0,
    double GreenLevelsWhitePoint = 1,
    double GreenLevelsGamma = 1,
    double BlueLevelsBlackPoint = 0,
    double BlueLevelsWhitePoint = 1,
    double BlueLevelsGamma = 1,
    RetouchProvenanceMap? RedProvenance = null,
    RetouchProvenanceMap? GreenProvenance = null,
    RetouchProvenanceMap? BlueProvenance = null);
