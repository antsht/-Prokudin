using Prokudin.Core.Crop;

namespace Prokudin.Core.Pipeline;

public sealed record CropSettings
{
    public bool UseManual { get; init; }

    public int ManualX0 { get; init; }

    public int ManualY0 { get; init; }

    public int ManualX1 { get; init; }

    public int ManualY1 { get; init; }

    public CropInfo? AutoInfo { get; set; }
}
