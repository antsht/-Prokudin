using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Pipeline;

public sealed record RgbBuildWithHistogramResult(
    RgbImageBuffer Rgb,
    CropInfo CropInfo,
    LevelsHistogramData LevelsHistogram);
