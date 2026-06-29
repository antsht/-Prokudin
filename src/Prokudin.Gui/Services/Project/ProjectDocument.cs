using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Services.Project;

public sealed class ProjectDocument
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? DisplayName { get; set; }

    public string? LinkedProjectPath { get; set; }

    public ProjectSourcePaths? SourcePaths { get; set; }

    public ProjectImportSettings Import { get; set; } = new();

    public ProjectAlignSettings Align { get; set; } = new();

    public ProjectCropSettings Crop { get; set; } = new();

    public ProjectColorSettings Color { get; set; } = new();

    public ProjectCleanSettings Clean { get; set; } = new();

    public ProjectSessionSettings Session { get; set; } = new();

    public RgbExportSettings? Export { get; set; }
}

public sealed class ProjectSourcePaths
{
    public string? Red { get; set; }

    public string? Green { get; set; }

    public string? Blue { get; set; }
}

public sealed class ProjectImportSettings
{
    public string TriptychOrder { get; set; } = "BGR";
}

public sealed class ProjectAlignSettings
{
    public ChannelName Reference { get; set; } = ChannelName.Green;

    public string Detector { get; set; } = "sift";

    public int MaxTranslation { get; set; } = 128;

    public int MaxFineIterations { get; set; } = 3;

    public int CoarseMaxSide { get; set; } = 1024;

    public bool TrimDarkBorders { get; set; }
}

public sealed class ProjectCropSettings
{
    public ProjectSelectionRect Selection { get; set; } = new();

    public bool LockSquare { get; set; }
}

public sealed class ProjectSelectionRect
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class ProjectColorSettings
{
    public bool AutoWhiteBalance { get; set; } = true;

    public double RedExposureStops { get; set; }

    public double GreenExposureStops { get; set; }

    public double BlueExposureStops { get; set; }

    public LevelsMode LevelsMode { get; set; } = LevelsMode.AutoPercentile;

    public double LevelsBlackPoint { get; set; }

    public double LevelsWhitePoint { get; set; } = 1.0;

    public double LevelsGamma { get; set; } = 1.0;

    public int Temperature { get; set; }

    public int Tint { get; set; }

    public int PipetteX { get; set; } = -1;

    public int PipetteY { get; set; } = -1;
}

public sealed class ProjectCleanSettings
{
    public AutoCleanQualityMode QualityMode { get; set; } = AutoCleanQualityMode.Quality;

    public int Sensitivity { get; set; } = 50;

    public int InpaintRadius { get; set; } = 3;

    public int PatchRadius { get; set; } = 3;

    public int SearchRadius { get; set; } = 48;

    public int SafetyRadius { get; set; } = 2;

    public int ContextRadius { get; set; } = 16;

    public int MinTrainingPixels { get; set; } = 64;

    public bool UseCrossChannelHealing { get; set; } = true;

    public bool UseTeleaHealing { get; set; }

    public bool UseLocalLinearPrediction { get; set; } = true;

    public bool UseGuidedPatchSearch { get; set; } = true;

    public bool UseRobustFit { get; set; } = true;

    public bool AutoMergeNearbyDefects { get; set; } = true;

    public int AutoMergeDistancePx { get; set; } = 3;

    public int AutoExpandHealingAreaPx { get; set; } = 2;

    public int MaxComponentArea { get; set; } = 5000;

    public float PredictionAlphaMin { get; set; } = 0.15f;

    public float PredictionAlphaMax { get; set; } = 0.75f;

    public float FeatherSigma { get; set; } = 1.5f;

    public float MaxAllowedError { get; set; } = 0.12f;

    public float LargeComponentConservativeScale { get; set; } = 0.5f;

    public bool DebugHealOutput { get; set; }

    public bool ShowHealMaskOverlay { get; set; } = true;

    public int BrushSize { get; set; } = 12;
}

public sealed class ProjectSessionSettings
{
    public WorkflowTool SelectedWorkflow { get; set; } = WorkflowTool.Import;

    public EditorToolMode ToolMode { get; set; } = EditorToolMode.Select;

    public PreviewZoomMode PreviewZoom { get; set; } = PreviewZoomMode.OneToOne;

    public bool OpenOutputFolderAfterExport { get; set; }
}
