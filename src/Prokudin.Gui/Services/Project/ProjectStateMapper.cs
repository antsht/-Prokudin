using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Services.Project;

public static class ProjectStateMapper
{
    public static ProjectDocument ToDocument(ProjectCapture capture, bool includeExportOverride)
    {
        return new ProjectDocument
        {
            FormatVersion = ProjectDocument.CurrentFormatVersion,
            SavedAt = DateTimeOffset.UtcNow,
            DisplayName = capture.DisplayName,
            LinkedProjectPath = capture.LinkedProjectPath,
            SourcePaths = new ProjectSourcePaths
            {
                Red = capture.RedSourcePath,
                Green = capture.GreenSourcePath,
                Blue = capture.BlueSourcePath,
            },
            Import = new ProjectImportSettings { TriptychOrder = capture.TriptychOrder },
            Align = new ProjectAlignSettings
            {
                Reference = capture.AlignReference,
                Detector = capture.AlignDetector,
                MaxTranslation = capture.AlignMaxTranslation,
                MaxFineIterations = capture.AlignMaxFineIterations,
                CoarseMaxSide = capture.AlignCoarseMaxSide,
                TrimDarkBorders = capture.TrimDarkBorders,
            },
            Crop = new ProjectCropSettings
            {
                Selection = ToSelection(capture.SelectionRect),
                LockSquare = capture.LockSquareSelection,
            },
            Color = new ProjectColorSettings
            {
                AutoWhiteBalance = capture.AutoWhiteBalance,
                WhiteBalanceSource = capture.WhiteBalanceSource,
                WhitePickRadius = capture.WhitePickRadius,
                WhitePickWarningAcknowledged = capture.WhitePickWarningAcknowledged,
                RedExposureStops = capture.RedExposureStops,
                GreenExposureStops = capture.GreenExposureStops,
                BlueExposureStops = capture.BlueExposureStops,
                LevelsMode = capture.LevelsMode,
                LevelsBlackPoint = capture.LevelsBlackPoint,
                LevelsWhitePoint = capture.LevelsWhitePoint,
                LevelsGamma = capture.LevelsGamma,
                RedLevelsBlackPoint = capture.RedLevelsBlackPoint,
                RedLevelsWhitePoint = capture.RedLevelsWhitePoint,
                RedLevelsGamma = capture.RedLevelsGamma,
                GreenLevelsBlackPoint = capture.GreenLevelsBlackPoint,
                GreenLevelsWhitePoint = capture.GreenLevelsWhitePoint,
                GreenLevelsGamma = capture.GreenLevelsGamma,
                BlueLevelsBlackPoint = capture.BlueLevelsBlackPoint,
                BlueLevelsWhitePoint = capture.BlueLevelsWhitePoint,
                BlueLevelsGamma = capture.BlueLevelsGamma,
                PipetteX = capture.PipetteX,
                PipetteY = capture.PipetteY,
                Temperature = capture.ColorTemperature,
                Tint = capture.ColorTint,
            },
            Clean = ToCleanSettings(capture),
            Session = new ProjectSessionSettings
            {
                SelectedWorkflow = capture.SelectedWorkflow,
                ToolMode = capture.ToolMode,
                PreviewZoom = capture.PreviewZoom,
                OpenOutputFolderAfterExport = capture.OpenOutputFolderAfterExport,
            },
            Export = includeExportOverride ? capture.ExportSettings.Normalize() : null,
        };
    }

    public static ProjectApplyState ToApplyState(ProjectDocument document) =>
        new()
        {
            DisplayName = document.DisplayName,
            RedSourcePath = document.SourcePaths?.Red,
            GreenSourcePath = document.SourcePaths?.Green,
            BlueSourcePath = document.SourcePaths?.Blue,
            TriptychOrder = document.Import.TriptychOrder,
            AlignReference = document.Align.Reference,
            AlignDetector = document.Align.Detector,
            AlignMaxTranslation = document.Align.MaxTranslation,
            AlignMaxFineIterations = document.Align.MaxFineIterations,
            AlignCoarseMaxSide = document.Align.CoarseMaxSide,
            TrimDarkBorders = document.Align.TrimDarkBorders,
            SelectionRect = FromSelection(document.Crop.Selection),
            LockSquareSelection = document.Crop.LockSquare,
            AutoWhiteBalance = document.Color.AutoWhiteBalance,
            WhiteBalanceSource = ResolveWhiteBalanceSource(document.Color),
            WhitePickRadius = Math.Clamp(document.Color.WhitePickRadius, 1, 25),
            WhitePickWarningAcknowledged = document.Color.WhitePickWarningAcknowledged,
            RedExposureStops = document.Color.RedExposureStops,
            GreenExposureStops = document.Color.GreenExposureStops,
            BlueExposureStops = document.Color.BlueExposureStops,
            LevelsMode = document.Color.LevelsMode,
            LevelsBlackPoint = document.Color.LevelsBlackPoint,
            LevelsWhitePoint = document.Color.LevelsWhitePoint,
            LevelsGamma = document.Color.LevelsGamma,
            RedLevelsBlackPoint = document.Color.RedLevelsBlackPoint,
            RedLevelsWhitePoint = document.Color.RedLevelsWhitePoint,
            RedLevelsGamma = document.Color.RedLevelsGamma,
            GreenLevelsBlackPoint = document.Color.GreenLevelsBlackPoint,
            GreenLevelsWhitePoint = document.Color.GreenLevelsWhitePoint,
            GreenLevelsGamma = document.Color.GreenLevelsGamma,
            BlueLevelsBlackPoint = document.Color.BlueLevelsBlackPoint,
            BlueLevelsWhitePoint = document.Color.BlueLevelsWhitePoint,
            BlueLevelsGamma = document.Color.BlueLevelsGamma,
            PipetteX = document.Color.PipetteX,
            PipetteY = document.Color.PipetteY,
            ColorTemperature = document.Color.Temperature,
            ColorTint = document.Color.Tint,
            Clean = FromCleanSettings(document.Clean),
            SelectedWorkflow = document.Session.SelectedWorkflow,
            ToolMode = document.Session.ToolMode,
            PreviewZoom = document.Session.PreviewZoom,
            OpenOutputFolderAfterExport = document.Session.OpenOutputFolderAfterExport,
            ExportSettings = document.Export,
        };

    private static WhiteBalanceSource ResolveWhiteBalanceSource(ProjectColorSettings color)
    {
        if (color.WhiteBalanceSource is { } source)
        {
            return source;
        }

        return !color.AutoWhiteBalance && color.PipetteX >= 0 && color.PipetteY >= 0
            ? WhiteBalanceSource.WhitePick
            : color.AutoWhiteBalance ? WhiteBalanceSource.Auto : WhiteBalanceSource.Off;
    }

    private static ProjectCleanSettings ToCleanSettings(ProjectCapture capture) =>
        new()
        {
            QualityMode = capture.AutoCleanQualityMode,
            Sensitivity = capture.AutoCleanSensitivity,
            InpaintRadius = capture.AutoCleanRadius,
            PatchRadius = capture.HealPatchRadius,
            SearchRadius = capture.HealSearchRadius,
            SafetyRadius = capture.HealSafetyRadius,
            ContextRadius = capture.HealContextRadius,
            MinTrainingPixels = capture.HealMinTrainingPixels,
            UseCrossChannelHealing = capture.UseCrossChannelHealing,
            UseTeleaHealing = capture.UseTeleaHealing,
            UseLocalLinearPrediction = capture.UseLocalLinearPrediction,
            UseGuidedPatchSearch = capture.UseGuidedPatchSearch,
            UseRobustFit = capture.UseRobustFit,
            AutoMergeNearbyDefects = capture.AutoMergeNearbyDefects,
            AutoMergeDistancePx = capture.AutoMergeDistancePx,
            AutoExpandHealingAreaPx = capture.AutoExpandHealingAreaPx,
            MaxComponentArea = capture.HealMaxComponentArea,
            PredictionAlphaMin = capture.HealPredictionAlphaMin,
            PredictionAlphaMax = capture.HealPredictionAlphaMax,
            FeatherSigma = capture.HealFeatherSigma,
            MaxAllowedError = capture.HealMaxAllowedError,
            LargeComponentConservativeScale = capture.HealLargeComponentConservativeScale,
            DebugHealOutput = capture.DebugHealOutput,
            ShowHealMaskOverlay = capture.ShowHealMaskOverlay,
            BrushSize = capture.BrushSize,
        };

    private static AutoCleanSettingsSnapshot FromCleanSettings(ProjectCleanSettings clean) =>
        new(
            QualityMode: clean.QualityMode,
            Sensitivity: clean.Sensitivity,
            InpaintRadius: clean.InpaintRadius,
            PatchRadius: clean.PatchRadius,
            SearchRadius: clean.SearchRadius,
            SafetyRadius: clean.SafetyRadius,
            ContextRadius: clean.ContextRadius,
            MinTrainingPixels: clean.MinTrainingPixels,
            UseCrossChannelHealing: clean.UseCrossChannelHealing,
            UseTeleaHealing: clean.UseTeleaHealing,
            UseLocalLinearPrediction: clean.UseLocalLinearPrediction,
            UseGuidedPatchSearch: clean.UseGuidedPatchSearch,
            UseRobustFit: clean.UseRobustFit,
            AutoMergeNearbyDefects: clean.AutoMergeNearbyDefects,
            AutoMergeDistancePx: clean.AutoMergeDistancePx,
            AutoExpandHealingAreaPx: clean.AutoExpandHealingAreaPx,
            MaxComponentArea: clean.MaxComponentArea,
            PredictionAlphaMin: clean.PredictionAlphaMin,
            PredictionAlphaMax: clean.PredictionAlphaMax,
            FeatherSigma: clean.FeatherSigma,
            MaxAllowedError: clean.MaxAllowedError,
            LargeComponentConservativeScale: clean.LargeComponentConservativeScale,
            DebugHealOutput: clean.DebugHealOutput,
            ShowHealMaskOverlay: clean.ShowHealMaskOverlay);

    private static ProjectSelectionRect ToSelection(ImageSelectionRect rect) =>
        new()
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
        };

    private static ImageSelectionRect FromSelection(ProjectSelectionRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);
}

public sealed class ProjectCapture
{
    public string? DisplayName { get; init; }

    public string? LinkedProjectPath { get; init; }

    public string? RedSourcePath { get; init; }

    public string? GreenSourcePath { get; init; }

    public string? BlueSourcePath { get; init; }

    public required string TriptychOrder { get; init; }

    public ChannelName AlignReference { get; init; }

    public required string AlignDetector { get; init; }

    public int AlignMaxTranslation { get; init; }

    public int AlignMaxFineIterations { get; init; }

    public int AlignCoarseMaxSide { get; init; }

    public bool TrimDarkBorders { get; init; }

    public ImageSelectionRect SelectionRect { get; init; }

    public bool LockSquareSelection { get; init; }

    public bool AutoWhiteBalance { get; init; }

    public WhiteBalanceSource WhiteBalanceSource { get; init; }

    public int WhitePickRadius { get; init; } = 3;

    public bool WhitePickWarningAcknowledged { get; init; }

    public double RedExposureStops { get; init; }

    public double GreenExposureStops { get; init; }

    public double BlueExposureStops { get; init; }

    public LevelsMode LevelsMode { get; init; }

    public double LevelsBlackPoint { get; init; }

    public double LevelsWhitePoint { get; init; }

    public double LevelsGamma { get; init; }

    public double RedLevelsBlackPoint { get; init; }

    public double RedLevelsWhitePoint { get; init; } = 1.0;

    public double RedLevelsGamma { get; init; } = 1.0;

    public double GreenLevelsBlackPoint { get; init; }

    public double GreenLevelsWhitePoint { get; init; } = 1.0;

    public double GreenLevelsGamma { get; init; } = 1.0;

    public double BlueLevelsBlackPoint { get; init; }

    public double BlueLevelsWhitePoint { get; init; } = 1.0;

    public double BlueLevelsGamma { get; init; } = 1.0;

    public int PipetteX { get; init; }

    public int PipetteY { get; init; }

    public int ColorTemperature { get; init; }

    public int ColorTint { get; init; }

    public AutoCleanQualityMode AutoCleanQualityMode { get; init; }

    public int AutoCleanSensitivity { get; init; }

    public int AutoCleanRadius { get; init; }

    public int HealPatchRadius { get; init; }

    public int HealSearchRadius { get; init; }

    public int HealSafetyRadius { get; init; }

    public int HealContextRadius { get; init; }

    public int HealMinTrainingPixels { get; init; }

    public bool UseCrossChannelHealing { get; init; }

    public bool UseTeleaHealing { get; init; }

    public bool UseLocalLinearPrediction { get; init; }

    public bool UseGuidedPatchSearch { get; init; }

    public bool UseRobustFit { get; init; }

    public bool AutoMergeNearbyDefects { get; init; }

    public int AutoMergeDistancePx { get; init; }

    public int AutoExpandHealingAreaPx { get; init; }

    public int HealMaxComponentArea { get; init; }

    public float HealPredictionAlphaMin { get; init; }

    public float HealPredictionAlphaMax { get; init; }

    public float HealFeatherSigma { get; init; }

    public float HealMaxAllowedError { get; init; }

    public float HealLargeComponentConservativeScale { get; init; }

    public bool DebugHealOutput { get; init; }

    public bool ShowHealMaskOverlay { get; init; }

    public int BrushSize { get; init; }

    public WorkflowTool SelectedWorkflow { get; init; }

    public EditorToolMode ToolMode { get; init; }

    public PreviewZoomMode PreviewZoom { get; init; }

    public bool OpenOutputFolderAfterExport { get; init; }

    public RgbExportSettings ExportSettings { get; init; } = RgbExportSettings.Default;
}

public sealed class ProjectApplyState
{
    public string? DisplayName { get; init; }

    public string? RedSourcePath { get; init; }

    public string? GreenSourcePath { get; init; }

    public string? BlueSourcePath { get; init; }

    public required string TriptychOrder { get; init; }

    public ChannelName AlignReference { get; init; }

    public required string AlignDetector { get; init; }

    public int AlignMaxTranslation { get; init; }

    public int AlignMaxFineIterations { get; init; }

    public int AlignCoarseMaxSide { get; init; }

    public bool TrimDarkBorders { get; init; }

    public ImageSelectionRect SelectionRect { get; init; }

    public bool LockSquareSelection { get; init; }

    public bool AutoWhiteBalance { get; init; }

    public WhiteBalanceSource WhiteBalanceSource { get; init; }

    public int WhitePickRadius { get; init; } = 3;

    public bool WhitePickWarningAcknowledged { get; init; }

    public double RedExposureStops { get; init; }

    public double GreenExposureStops { get; init; }

    public double BlueExposureStops { get; init; }

    public LevelsMode LevelsMode { get; init; }

    public double LevelsBlackPoint { get; init; }

    public double LevelsWhitePoint { get; init; }

    public double LevelsGamma { get; init; }

    public double RedLevelsBlackPoint { get; init; }

    public double RedLevelsWhitePoint { get; init; } = 1.0;

    public double RedLevelsGamma { get; init; } = 1.0;

    public double GreenLevelsBlackPoint { get; init; }

    public double GreenLevelsWhitePoint { get; init; } = 1.0;

    public double GreenLevelsGamma { get; init; } = 1.0;

    public double BlueLevelsBlackPoint { get; init; }

    public double BlueLevelsWhitePoint { get; init; } = 1.0;

    public double BlueLevelsGamma { get; init; } = 1.0;

    public int PipetteX { get; init; }

    public int PipetteY { get; init; }

    public int ColorTemperature { get; init; }

    public int ColorTint { get; init; }

    public AutoCleanSettingsSnapshot Clean { get; init; } = new();

    public WorkflowTool SelectedWorkflow { get; init; }

    public EditorToolMode ToolMode { get; init; }

    public PreviewZoomMode PreviewZoom { get; init; }

    public bool OpenOutputFolderAfterExport { get; init; }

    public RgbExportSettings? ExportSettings { get; init; }
}
