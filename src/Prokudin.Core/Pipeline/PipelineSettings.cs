using Prokudin.Core.Alignment;
using Prokudin.Core.Color;

namespace Prokudin.Core.Pipeline;

public sealed record PipelineSettings
{
    public AlignOptions Align { get; init; } = new();

    public ColorSettings Color { get; init; } = new();

    public CropSettings Crop { get; init; } = new();

    public bool Sharpen { get; init; } = true;

    public int? OutputSize { get; init; }
}
