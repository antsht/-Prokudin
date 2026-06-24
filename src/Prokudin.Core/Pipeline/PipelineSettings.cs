using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Diagnostics;

namespace Prokudin.Core.Pipeline;

public sealed record PipelineSettings
{
    public AlignOptions Align { get; init; } = new();

    public ColorSettings Color { get; init; } = new();

    public ChannelExposureSettings Exposure { get; init; } = new();

    public CropSettings Crop { get; init; } = new();

    public LevelsSettings Levels { get; init; } = new();

    public bool Sharpen { get; init; } = true;

    public int? OutputSize { get; init; }

    public IProcessingDiagnostics? Diagnostics { get; init; }
}
