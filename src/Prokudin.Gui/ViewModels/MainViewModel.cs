using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Diagnostics;
using Prokudin.Gui.Editing;
using Prokudin.Gui.Imaging;
using Prokudin.Gui.Services;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.Views;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int DefaultWhitePickRadius = 3;
    private const int MaxProcessingLogCharacters = 120_000;
    private readonly IFileDialogService fileDialogService;
    private readonly IExportSettingsStore exportSettingsStore;
    private readonly IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore;
    private readonly IAutoCleanSettingsStore autoCleanSettingsStore;
    private readonly IUiSettingsStore uiSettingsStore;
    private readonly IUpdateChecker updateChecker;
    private readonly IProjectStore projectStore;
    private readonly IAutosaveStore autosaveStore;
    private readonly IRecentProjectsStore recentProjectsStore;
    private readonly StringBuilder processingLog = new();
    private readonly object processingLogSync = new();
    private bool processingLogRefreshScheduled;
    private AlignedChannels? lastAligned;
    private CancellationTokenSource? resultRebuildCancellation;
    private bool isRestoringSnapshot;
    private bool suppressUndoCapture;
    private bool colorCoalesceOpen;
    private bool suppressExportSettingsSave;
    private bool suppressDiagnosticsSettingsSave;
    private bool suppressAutoCleanSettingsSave;
    private bool suppressUiSettingsSave;
    private int colorChangeVersion;
    private int previewImageContextVersion;
    private Bitmap? autoCleanMaskOverlayBitmap;
    private byte[]? pendingAutoCleanBaseMask;
    private byte[]? pendingAutoCleanAddMask;
    private byte[]? pendingAutoCleanRemoveMask;
    private CancellationTokenSource? autoCleanMaskRefreshCancellation;
    private int autoCleanProgressVersion;
    private int whiteBalancePipetteX = -1;
    private int whiteBalancePipetteY = -1;
    private readonly AutoCleanSessionCache autoCleanSessionCache = new();
    private Window? ownerWindow;
    private bool suppressProjectDirtyTracking;
    private bool isAutosavePending;
    private DispatcherTimer? autosaveTimer;

    public MainViewModel(IFileDialogService fileDialogService)
        : this(fileDialogService, new JsonExportSettingsStore(), new JsonProcessingDiagnosticsSettingsStore(), new JsonAutoCleanSettingsStore(), new JsonUiSettingsStore())
    {
    }

    public MainViewModel(IFileDialogService fileDialogService, IExportSettingsStore exportSettingsStore)
        : this(fileDialogService, exportSettingsStore, new JsonProcessingDiagnosticsSettingsStore(), new JsonAutoCleanSettingsStore(), new JsonUiSettingsStore())
    {
    }

    public MainViewModel(
        IFileDialogService fileDialogService,
        IExportSettingsStore exportSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore)
        : this(fileDialogService, exportSettingsStore, diagnosticsSettingsStore, new JsonAutoCleanSettingsStore(), new JsonUiSettingsStore())
    {
    }

    public MainViewModel(
        IFileDialogService fileDialogService,
        IExportSettingsStore exportSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore,
        IAutoCleanSettingsStore autoCleanSettingsStore)
        : this(fileDialogService, exportSettingsStore, diagnosticsSettingsStore, autoCleanSettingsStore, new JsonUiSettingsStore())
    {
    }

    public MainViewModel(
        IFileDialogService fileDialogService,
        IExportSettingsStore exportSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore,
        IAutoCleanSettingsStore autoCleanSettingsStore,
        IUiSettingsStore uiSettingsStore)
        : this(fileDialogService, exportSettingsStore, diagnosticsSettingsStore, autoCleanSettingsStore, uiSettingsStore, new GitHubReleaseUpdateChecker(), new JsonProjectStore(), new JsonAutosaveStore(), new JsonRecentProjectsStore())
    {
    }

    public MainViewModel(
        IFileDialogService fileDialogService,
        IExportSettingsStore exportSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore,
        IAutoCleanSettingsStore autoCleanSettingsStore,
        IUiSettingsStore uiSettingsStore,
        IUpdateChecker updateChecker)
        : this(fileDialogService, exportSettingsStore, diagnosticsSettingsStore, autoCleanSettingsStore, uiSettingsStore, updateChecker, new JsonProjectStore(), new JsonAutosaveStore(), new JsonRecentProjectsStore())
    {
    }

    public MainViewModel(
        IFileDialogService fileDialogService,
        IExportSettingsStore exportSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore,
        IAutoCleanSettingsStore autoCleanSettingsStore,
        IUiSettingsStore uiSettingsStore,
        IUpdateChecker updateChecker,
        IProjectStore projectStore,
        IAutosaveStore autosaveStore,
        IRecentProjectsStore recentProjectsStore)
    {
        this.fileDialogService = fileDialogService;
        this.exportSettingsStore = exportSettingsStore;
        this.diagnosticsSettingsStore = diagnosticsSettingsStore;
        this.autoCleanSettingsStore = autoCleanSettingsStore;
        this.uiSettingsStore = uiSettingsStore;
        this.updateChecker = updateChecker;
        this.projectStore = projectStore;
        this.autosaveStore = autosaveStore;
        this.recentProjectsStore = recentProjectsStore;

        RedSlot = new ChannelSlotViewModel("Red", ChannelName.Red);
        GreenSlot = new ChannelSlotViewModel("Green", ChannelName.Green);
        BlueSlot = new ChannelSlotViewModel("Blue", ChannelName.Blue);
        ResultSlot = new ChannelSlotViewModel("Result", null);

        Slots = new ObservableCollection<ChannelSlotViewModel>
        {
            RedSlot,
            GreenSlot,
            BlueSlot,
            ResultSlot,
        };

        RedSlot.OpenCommand = OpenRedCommand;
        GreenSlot.OpenCommand = OpenGreenCommand;
        BlueSlot.OpenCommand = OpenBlueCommand;
        ResultSlot.ExportCommand = ExportCommand;
        ResultSlot.ToggleExportSettingsCommand = ToggleExportSettingsCommand;

        LoadExportSettings(exportSettingsStore.Load());
        LoadDiagnosticsSettings(diagnosticsSettingsStore.Load());
        LoadAutoCleanSettings(autoCleanSettingsStore.Load());
        LoadUiSettings(uiSettingsStore.Load());
        SelectedSlot = RedSlot;
        RefreshRecentProjectsMenu();
        ConfigureAutosaveTimer();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand))]
    private string? projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand))]
    private bool isProjectDirty;

    [ObservableProperty]
    private bool isAutosaving;

    public ObservableCollection<RecentProjectEntry> RecentProjectsMenu { get; } = [];

    public string WindowTitle =>
        $"{(string.IsNullOrWhiteSpace(ProjectDisplayName) ? "Untitled" : ProjectDisplayName)}{(IsProjectDirty ? "*" : string.Empty)} — Prokudin";

    public string? ProjectDisplayName =>
        string.IsNullOrWhiteSpace(ProjectPath)
            ? null
            : Path.GetFileName(ProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public void AttachOwnerWindow(Window window) => ownerWindow = window;

    public double LeftPanelWidthClamped => Math.Clamp(LeftPanelWidth, 220, 420);

    public double RightInspectorWidthClamped => Math.Clamp(RightInspectorWidth, 300, 520);

    public double ProcessingLogHeightClamped => Math.Clamp(ProcessingLogHeight, 44, 360);

    public bool IsLightThemeSelected
    {
        get => AppThemeMode == AppThemeMode.Light;
        set
        {
            if (value && AppThemeMode != AppThemeMode.Light)
            {
                AppThemeMode = AppThemeMode.Light;
            }
        }
    }

    public bool IsDarkThemeSelected
    {
        get => AppThemeMode == AppThemeMode.Dark;
        set
        {
            if (value && AppThemeMode != AppThemeMode.Dark)
            {
                AppThemeMode = AppThemeMode.Dark;
            }
        }
    }

    public bool IsSystemThemeSelected
    {
        get => AppThemeMode == AppThemeMode.System;
        set
        {
            if (value && AppThemeMode != AppThemeMode.System)
            {
                AppThemeMode = AppThemeMode.System;
            }
        }
    }

    public ChannelSlotViewModel RedSlot { get; }

    public ChannelSlotViewModel GreenSlot { get; }

    public ChannelSlotViewModel BlueSlot { get; }

    public ChannelSlotViewModel ResultSlot { get; }

    public ObservableCollection<ChannelSlotViewModel> Slots { get; }

    public IReadOnlyList<string> TriptychOrders { get; } = ["RGB", "BGR"];

    public IReadOnlyList<LevelsMode> LevelsModes { get; } = Enum.GetValues<LevelsMode>();

    public IReadOnlyList<WhiteBalanceSource> WhiteBalanceSources { get; } = Enum.GetValues<WhiteBalanceSource>();

    public IReadOnlyList<LevelsScope> LevelsScopes { get; } = Enum.GetValues<LevelsScope>();

    public bool AutoWhiteBalance
    {
        get => WhiteBalanceSource == global::Prokudin.Core.Color.WhiteBalanceSource.Auto;
        set => WhiteBalanceSource = value
            ? global::Prokudin.Core.Color.WhiteBalanceSource.Auto
            : global::Prokudin.Core.Color.WhiteBalanceSource.Off;
    }

    public IReadOnlyList<RgbExportFormat> ExportFormats { get; } =
        [RgbExportFormat.Png, RgbExportFormat.Jpeg, RgbExportFormat.Tiff];

    public IReadOnlyList<TiffExportCompression> TiffCompressions { get; } =
        [TiffExportCompression.None, TiffExportCompression.Lzw, TiffExportCompression.Deflate];

    public IReadOnlyList<AutoCleanQualityMode> AutoCleanQualityModes { get; } =
        Enum.GetValues<AutoCleanQualityMode>();

    [ObservableProperty]
    private AutoCleanQualityMode autoCleanQualityMode = AutoCleanQualityMode.Quality;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CropToSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoCleanSelectedChannelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAutoCleanMaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAutoCleanMaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditAutoCleanMaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRetouchStrokeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStampStrokeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickWhiteBalanceCommand))]
    [NotifyPropertyChangedFor(nameof(CanUseWhiteBalancePicker))]
    [NotifyPropertyChangedFor(nameof(PreviewImageContextKey))]
    [NotifyPropertyChangedFor(nameof(PreviewDisplayBitmap))]
    [NotifyPropertyChangedFor(nameof(PreviewHasImage))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotSummary))]
    private ChannelSlotViewModel? selectedSlot;

    [ObservableProperty]
    private string selectedTriptychOrder = "BGR";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenGreenCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBlueCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTriptychCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoAlignCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoCleanSelectedChannelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAutoCleanMaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAutoCleanMaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditAutoCleanMaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRetouchStrokeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStampStrokeCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportChannelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickWhiteBalanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildResultCommand))]
    [NotifyCanExecuteChangedFor(nameof(CropOverlapCommand))]
    [NotifyPropertyChangedFor(nameof(CanUseWhiteBalancePicker))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotSummary))]
    [NotifyPropertyChangedFor(nameof(BusyIndicatorText))]
    [NotifyPropertyChangedFor(nameof(IsUiEnabled))]
    private bool isBusy;

    public bool IsUiEnabled => !IsBusy;

    [ObservableProperty]
    private string status = "Open three channels or a triptych to begin.";

    public string SelectedSlotSummary =>
        SelectedSlot is { } slot
            ? $"Selected: {slot.DisplayName} {slot.Dimensions}"
            : "Selected: —";

    public string BusyIndicatorText => IsBusy ? "Busy" : "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportWorkflow))]
    [NotifyPropertyChangedFor(nameof(IsAlignWorkflow))]
    [NotifyPropertyChangedFor(nameof(IsCropWorkflow))]
    [NotifyPropertyChangedFor(nameof(IsCleanWorkflow))]
    [NotifyPropertyChangedFor(nameof(IsColorWorkflow))]
    [NotifyPropertyChangedFor(nameof(IsExportWorkflow))]
    private WorkflowTool selectedWorkflowTool = WorkflowTool.Import;

    public bool IsImportWorkflow => SelectedWorkflowTool == WorkflowTool.Import;

    public bool IsAlignWorkflow => SelectedWorkflowTool == WorkflowTool.Align;

    public bool IsCropWorkflow => SelectedWorkflowTool == WorkflowTool.Crop;

    public bool IsCleanWorkflow => SelectedWorkflowTool == WorkflowTool.Clean;

    public bool IsColorWorkflow => SelectedWorkflowTool == WorkflowTool.Color;

    public bool IsExportWorkflow => SelectedWorkflowTool == WorkflowTool.Export;

    [ObservableProperty]
    private ChannelName alignReference = ChannelName.Green;

    [ObservableProperty]
    private string alignDetector = "sift";

    [ObservableProperty]
    private int alignMaxTranslation = 128;

    [ObservableProperty]
    private int alignMaxFineIterations = 3;

    [ObservableProperty]
    private int alignCoarseMaxSide = 1024;

    [ObservableProperty]
    private bool trimDarkBorders;

    [ObservableProperty]
    private LevelsMode levelsMode = LevelsMode.AutoPercentile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMasterLevelsScope))]
    [NotifyPropertyChangedFor(nameof(IsChannelLevelsScope))]
    [NotifyPropertyChangedFor(nameof(ActiveLevelsMode))]
    [NotifyPropertyChangedFor(nameof(ActiveLevelsBlackPoint))]
    [NotifyPropertyChangedFor(nameof(ActiveLevelsWhitePoint))]
    [NotifyPropertyChangedFor(nameof(ActiveLevelsGamma))]
    private LevelsScope levelsScope = LevelsScope.Master;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualLevels))]
    private double levelsBlackPoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualLevels))]
    private double levelsWhitePoint = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualLevels))]
    private double levelsGamma = 1.0;

    [ObservableProperty] private double redLevelsBlackPoint;
    [ObservableProperty] private double redLevelsWhitePoint = 1.0;
    [ObservableProperty] private double redLevelsGamma = 1.0;
    [ObservableProperty] private double greenLevelsBlackPoint;
    [ObservableProperty] private double greenLevelsWhitePoint = 1.0;
    [ObservableProperty] private double greenLevelsGamma = 1.0;
    [ObservableProperty] private double blueLevelsBlackPoint;
    [ObservableProperty] private double blueLevelsWhitePoint = 1.0;
    [ObservableProperty] private double blueLevelsGamma = 1.0;

    [ObservableProperty]
    private bool openOutputFolderAfterExport;

    [ObservableProperty]
    private bool showHealMaskOverlay = true;

    [ObservableProperty]
    private bool useLocalLinearPrediction = true;

    [ObservableProperty]
    private bool useGuidedPatchSearch = true;

    [ObservableProperty]
    private bool useRobustFit = true;

    [ObservableProperty]
    private int healPatchRadius = 3;

    [ObservableProperty]
    private int healSearchRadius = 48;

    [ObservableProperty]
    private int healSafetyRadius = 2;

    [ObservableProperty]
    private int healContextRadius = 16;

    [ObservableProperty]
    private int healMinTrainingPixels = 64;

    [ObservableProperty]
    private int healMaxComponentArea = 5000;

    [ObservableProperty]
    private double healPredictionAlphaMin = 0.15;

    [ObservableProperty]
    private double healPredictionAlphaMax = 0.75;

    [ObservableProperty]
    private double healFeatherSigma = 1.5;

    [ObservableProperty]
    private double healMaxAllowedError = 0.12;

    [ObservableProperty]
    private double healLargeComponentConservativeScale = 0.5;

    [ObservableProperty]
    private AppThemeMode appThemeMode = AppThemeMode.System;

    [ObservableProperty]
    private bool isLeftPanelVisible = true;

    [ObservableProperty]
    private bool isRightInspectorVisible = true;

    [ObservableProperty]
    private bool isProcessingLogVisible = true;

    [ObservableProperty]
    private double leftPanelWidth = 260;

    [ObservableProperty]
    private double rightInspectorWidth = 360;

    [ObservableProperty]
    private double processingLogHeight = 150;

    public bool IsManualLevels => LevelsMode == LevelsMode.Manual;

    public bool IsMasterLevelsScope => LevelsScope == LevelsScope.Master;

    public bool IsChannelLevelsScope => !IsMasterLevelsScope;

    public bool CanEditActiveLevels => IsChannelLevelsScope || IsManualLevels;

    public LevelsMode ActiveLevelsMode
    {
        get => IsMasterLevelsScope ? LevelsMode : LevelsMode.Manual;
        set
        {
            if (IsMasterLevelsScope)
            {
                LevelsMode = value;
            }
        }
    }

    public string InspectorChannelLabel => SelectedSlot?.DisplayName ?? "—";

    public string InspectorSizeLabel => SelectedSlot?.Dimensions ?? "—";

    public string InspectorStateLabel => SelectedSlot?.StateLabel ?? "—";

    public string InputModeLabel =>
        RedSlot.SourcePath is { Length: > 0 } path &&
        path == GreenSlot.SourcePath &&
        path == BlueSlot.SourcePath
            ? "Triptych"
            : RedSlot.HasImage || GreenSlot.HasImage || BlueSlot.HasImage
                ? "Separate channels"
                : "—";

    public string SelectedSlotSourceBitDepth =>
        SelectedSlot?.Image?.Format.ToString()
        ?? (SelectedSlot?.Result is not null ? "UInt8 (RGB pipeline)" : "—");

    public string CropBehaviorHint =>
        SelectedSlot?.IsResultSlot == true
            ? "Cropping result also crops prepared R/G/B channels."
            : SelectedSlot?.Image is not null
                ? "Cropping applies to the selected channel only."
                : "Select a channel or result, then draw a selection on the preview.";

    public string RedAlignSummary => FormatAlignSummary(ChannelName.Red);

    public string GreenAlignSummary => FormatAlignSummary(ChannelName.Green);

    public string BlueAlignSummary => FormatAlignSummary(ChannelName.Blue);

    public int SelectionX
    {
        get => SelectionRect.X;
        set
        {
            if (SelectionRect.X == value)
            {
                return;
            }

            SelectionRect = SelectionRect with { X = value };
        }
    }

    public int SelectionY
    {
        get => SelectionRect.Y;
        set
        {
            if (SelectionRect.Y == value)
            {
                return;
            }

            SelectionRect = SelectionRect with { Y = value };
        }
    }

    public int SelectionWidth
    {
        get => SelectionRect.Width;
        set
        {
            if (SelectionRect.Width == value)
            {
                return;
            }

            SelectionRect = SelectionRect with { Width = value };
        }
    }

    public int SelectionHeight
    {
        get => SelectionRect.Height;
        set
        {
            if (SelectionRect.Height == value)
            {
                return;
            }

            SelectionRect = SelectionRect with { Height = value };
        }
    }

    [ObservableProperty]
    private bool lockSquareSelection;

    public IReadOnlyList<ChannelName> AlignReferenceChannels { get; } =
        [ChannelName.Red, ChannelName.Green, ChannelName.Blue];

    public IReadOnlyList<string> AlignDetectors { get; } = ["sift", "orb"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitToWindowButtonText))]
    private PreviewZoomMode previewZoomMode = PreviewZoomMode.OneToOne;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CropToSelectionCommand))]
    private ImageSelectionRect selectionRect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewInteractionMode))]
    [NotifyPropertyChangedFor(nameof(SelectedRetouchTool))]
    [NotifyPropertyChangedFor(nameof(IsWhiteBalancePickerToolMode))]
    private EditorToolMode toolMode = EditorToolMode.Select;

    [ObservableProperty]
    private int brushSize = 12;

    [ObservableProperty]
    private int autoCleanSensitivity = 50;

    [ObservableProperty]
    private int autoCleanRadius = 3;

    [ObservableProperty]
    private int autoExpandHealingAreaPx = 2;

    [ObservableProperty]
    private bool autoMergeNearbyDefects = true;

    [ObservableProperty]
    private int autoMergeDistancePx = 3;

    [ObservableProperty]
    private bool isAutoCleanProgressVisible;

    [ObservableProperty]
    private double autoCleanProgress;

    [ObservableProperty]
    private bool useCrossChannelHealing = true;

    [ObservableProperty]
    private bool useTeleaHealing;

    [ObservableProperty]
    private bool debugHealOutput;

    [ObservableProperty]
    private bool logComputeBackends;

    [ObservableProperty]
    private bool logPipelineStages;

    [ObservableProperty]
    private bool logCpuParallel;

    [ObservableProperty]
    private bool logTimings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoCleanMaskPending))]
    [NotifyPropertyChangedFor(nameof(PreviewInteractionMode))]
    [NotifyPropertyChangedFor(nameof(CanUseWhiteBalancePicker))]
    private byte[]? pendingAutoCleanMask;

    [ObservableProperty]
    private ChannelName? pendingAutoCleanChannel;

    [ObservableProperty]
    private int pendingAutoCleanCandidatePixels;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewDisplayBitmap))]
    [NotifyPropertyChangedFor(nameof(PreviewHasImage))]
    private bool showAutoCleanResultPreview;

    [ObservableProperty]
    private WhiteBalanceSource whiteBalanceSource = WhiteBalanceSource.Auto;

    [ObservableProperty]
    private int whitePickRadius = DefaultWhitePickRadius;

    [ObservableProperty]
    private bool whitePickWarningAcknowledged;

    [ObservableProperty]
    private double redExposureStops;

    [ObservableProperty]
    private double greenExposureStops;

    [ObservableProperty]
    private double blueExposureStops;

    [ObservableProperty]
    private int colorTemperature;

    [ObservableProperty]
    private int colorTint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLoupeEnabled))]
    private bool isLoupeEnabled;

    [ObservableProperty]
    private bool isExportSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPngExport))]
    [NotifyPropertyChangedFor(nameof(IsJpegExport))]
    [NotifyPropertyChangedFor(nameof(IsTiffExport))]
    [NotifyPropertyChangedFor(nameof(IsTiffDeflate))]
    [NotifyPropertyChangedFor(nameof(ExportSettingsSummary))]
    private RgbExportFormat exportFormat = RgbExportSettings.Default.Format;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportSettingsSummary))]
    private bool limitExportSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportSettingsSummary))]
    private int exportMaxSide = 2048;

    [ObservableProperty]
    private int pngCompression = RgbExportSettings.Default.PngCompression;

    [ObservableProperty]
    private int jpegQuality = RgbExportSettings.Default.JpegQuality;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTiffDeflate))]
    private TiffExportCompression tiffCompression = RgbExportSettings.Default.TiffCompression;

    [ObservableProperty]
    private int tiffDeflateLevel = RgbExportSettings.Default.TiffDeflateLevel;

    public PreviewInteractionMode PreviewInteractionMode =>
        IsAutoCleanMaskPending
            ? PreviewInteractionMode.MaskReview
            : ToolMode switch
            {
                EditorToolMode.Select => PreviewInteractionMode.Selection,
                EditorToolMode.WhiteBalancePicker => PreviewInteractionMode.WhiteBalancePicker,
                _ => PreviewInteractionMode.Retouch,
            };

    public RetouchTool SelectedRetouchTool =>
        ToolMode == EditorToolMode.Clone ? RetouchTool.Stamp : RetouchTool.Heal;

    public bool CanUseWhiteBalancePicker => !IsBusy && !IsAutoCleanMaskPending && ResultSlot.Result is not null;

    public bool IsSelectToolMode
    {
        get => ToolMode == EditorToolMode.Select;
        set
        {
            if (value)
            {
                ToolMode = EditorToolMode.Select;
            }
        }
    }

    public bool IsHealToolMode
    {
        get => ToolMode == EditorToolMode.Heal;
        set
        {
            if (value)
            {
                ToolMode = EditorToolMode.Heal;
            }
        }
    }

    public bool IsCloneToolMode
    {
        get => ToolMode == EditorToolMode.Clone;
        set
        {
            if (value)
            {
                ToolMode = EditorToolMode.Clone;
            }
        }
    }

    public bool IsWhiteBalancePickerToolMode
    {
        get => ToolMode == EditorToolMode.WhiteBalancePicker;
        set
        {
            if (value && CanUseWhiteBalancePicker)
            {
                SelectedSlot = ResultSlot;
                ToolMode = EditorToolMode.WhiteBalancePicker;
            }
        }
    }

    public bool IsPngExport => ExportFormat == RgbExportFormat.Png;

    public bool IsJpegExport => ExportFormat == RgbExportFormat.Jpeg;

    public bool IsTiffExport => ExportFormat == RgbExportFormat.Tiff;

    public bool IsTiffDeflate => IsTiffExport && TiffCompression == TiffExportCompression.Deflate;

    public string ExportSettingsSummary =>
        $"{ExportFormat} - {(LimitExportSize ? $"max {ExportMaxSide}px" : "original")}";

    public string FitToWindowButtonText =>
        PreviewZoomMode == PreviewZoomMode.FitToWindow ? "1:1" : "Fit to window";

    public string ProcessingLogText
    {
        get
        {
            lock (processingLogSync)
            {
                return processingLog.ToString();
            }
        }
    }

    public string PreviewImageContextKey => $"{SelectedSlot?.DisplayName ?? "none"}:{previewImageContextVersion}";

    public bool IsAutoCleanMaskPending => PendingAutoCleanMask is not null && PendingAutoCleanChannel.HasValue;

    public Bitmap? PreviewDisplayBitmap =>
        IsAutoCleanMaskPending && ShowAutoCleanResultPreview && ResultSlot.DisplayBitmap is not null
            ? ResultSlot.DisplayBitmap
            : SelectedSlot?.DisplayBitmap;

    public bool PreviewHasImage => PreviewDisplayBitmap is not null;

    public Bitmap? AutoCleanMaskOverlayBitmap
    {
        get => autoCleanMaskOverlayBitmap;
        private set
        {
            if (ReferenceEquals(autoCleanMaskOverlayBitmap, value))
            {
                return;
            }

            var previous = autoCleanMaskOverlayBitmap;
            if (SetProperty(ref autoCleanMaskOverlayBitmap, value))
            {
                previous?.Dispose();
            }
            else
            {
                value?.Dispose();
            }
        }
    }

    [RelayCommand]
    private void SelectWorkflowTool(WorkflowTool tool)
    {
        SelectedWorkflowTool = tool;
    }

    [RelayCommand]
    private void ToggleFitToWindow()
    {
        PreviewZoomMode = PreviewZoomMode == PreviewZoomMode.OneToOne
            ? PreviewZoomMode.FitToWindow
            : PreviewZoomMode.OneToOne;
        AppendLog($"Preview zoom: {PreviewZoomMode}.");
    }

    [RelayCommand]
    private void ClearLog()
    {
        lock (processingLogSync)
        {
            processingLog.Clear();
        }

        NotifyProcessingLogChanged();
    }

    [RelayCommand]
    private async Task Exit()
    {
        if (!await TryCloseSessionAsync())
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private async Task ShowAbout()
    {
        if (ownerWindow is null)
        {
            return;
        }

        await new AboutDialog().ShowDialog(ownerWindow);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (ownerWindow is null)
        {
            return;
        }

        Status = "Checking for updates...";
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        var result = await Task.Run(() => updateChecker.CheckForUpdatesAsync(currentVersion)).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            Status = $"Update check failed: {result.ErrorMessage}";
            AppendLog(Status);
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            Status = "You are up to date.";
            AppendLog(Status);
            return;
        }

        Status = result.LatestVersion is null
            ? "A newer release is available on GitHub."
            : $"Update available: {result.LatestVersion}.";
        AppendLog(Status);
        await new UpdateAvailableDialog(result).ShowDialog(ownerWindow);
    }

    [RelayCommand]
    private async Task ShowKeyboardShortcuts()
    {
        if (ownerWindow is null)
        {
            return;
        }

        await new KeyboardShortcutsDialog().ShowDialog(ownerWindow);
    }

    [RelayCommand]
    private void OpenUserGuide()
    {
        foreach (var path in EnumerateUserGuideCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
                AppendLog($"Opened user guide: {path}");
                return;
            }
            catch (Exception ex)
            {
                AppendLog($"Could not open user guide: {ex.Message}");
                Status = ex.Message;
                return;
            }
        }

        Status = "User guide not found. See docs/user-guide.md in the repository.";
        AppendLog(Status);
    }

    [RelayCommand]
    private void SetPreviewFitToWindow()
    {
        PreviewZoomMode = PreviewZoomMode.FitToWindow;
        AppendLog($"Preview zoom: {PreviewZoomMode}.");
    }

    [RelayCommand]
    private void SetPreviewOneToOne()
    {
        PreviewZoomMode = PreviewZoomMode.OneToOne;
        AppendLog($"Preview zoom: {PreviewZoomMode}.");
    }

    [RelayCommand]
    private void SelectTool(EditorToolMode mode)
    {
        if (mode == EditorToolMode.WhiteBalancePicker && !CanUseWhiteBalancePicker)
        {
            return;
        }

        ToolMode = mode;
    }

    private static IEnumerable<string> EnumerateUserGuideCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "docs", "user-guide.md");

        var directory = AppContext.BaseDirectory;
        for (var depth = 0; depth < 6; depth++)
        {
            directory = Path.GetFullPath(Path.Combine(directory, ".."));
            yield return Path.Combine(directory, "docs", "user-guide.md");
        }
    }

    [RelayCommand]
    private void ToggleExportSettings()
    {
        IsExportSettingsOpen = !IsExportSettingsOpen;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        await RunOperation(async () =>
        {
            if (HasUncommittedManualNudge)
            {
                Status = "Export uses last committed alignment; commit or reset the manual nudge first.";
                AppendLog("Warning: uncommitted manual alignment nudge — export reflects last rebuild preview, not baked channels.");
            }

            var settings = CurrentExportSettings();
            SaveExportSettings();
            var path = await fileDialogService.SaveExport(settings);
            var result = ResultSlot.Result;
            if (path is null || result is null)
            {
                return;
            }

            Status = $"Exporting {Path.GetFileName(path)}...";
            AppendLog($"Exporting {result.Width}x{result.Height} as {settings.Format} to {path}");
            await Task.Run(async () => await ImageLoader.SaveRgbAsync(path, result, settings));
            Status = $"Exported {path}.";
            AppendLog($"Export complete: {path}");
            if (OpenOutputFolderAfterExport)
            {
                TryOpenExportFolder(path);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanExportChannels))]
    private async Task ExportChannels()
    {
        await RunOperation(async () =>
        {
            var folder = await fileDialogService.OpenFolder();
            if (folder is null ||
                RedSlot.Image is not { } red ||
                GreenSlot.Image is not { } green ||
                BlueSlot.Image is not { } blue)
            {
                return;
            }

            Status = $"Exporting channels to {folder}...";
            AppendLog($"Exporting working channels to {folder}");
            await Task.Run(async () =>
            {
                await ImageLoader.SaveGrayscalePngAsync(Path.Combine(folder, "red.png"), red);
                await ImageLoader.SaveGrayscalePngAsync(Path.Combine(folder, "green.png"), green);
                await ImageLoader.SaveGrayscalePngAsync(Path.Combine(folder, "blue.png"), blue);
            });

            Status = $"Exported channels to {folder}.";
            AppendLog($"Channel export complete: {folder}");
            if (OpenOutputFolderAfterExport)
            {
                TryOpenExportFolder(folder);
            }
        });
    }

    private async Task RunOperation(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            AutoAlignCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            ExportChannelsCommand.NotifyCanExecuteChanged();
            CropToSelectionCommand.NotifyCanExecuteChanged();
            AutoCleanSelectedChannelCommand.NotifyCanExecuteChanged();
            ApplyAutoCleanMaskCommand.NotifyCanExecuteChanged();
            CancelAutoCleanMaskCommand.NotifyCanExecuteChanged();
            EditAutoCleanMaskCommand.NotifyCanExecuteChanged();
            ApplyRetouchStrokeCommand.NotifyCanExecuteChanged();
            ApplyStampStrokeCommand.NotifyCanExecuteChanged();
            NotifyHistoryCommands();
        }
    }

    private void SetChannel(ChannelSlotViewModel slot, ImageBuffer image, string sourcePath)
    {
        ClearPendingAutoCleanMask();
        slot.Image = image;
        slot.SourcePath = sourcePath;
        RefreshPreviewImageContext();
        RefreshChannelStates();
    }

    private void SetPreparedChannels(AlignedChannels aligned)
    {
        ClearPendingAutoCleanMask();
        RedSlot.Image = aligned.Red;
        GreenSlot.Image = aligned.Green;
        BlueSlot.Image = aligned.Blue;
        RefreshPreviewImageContext();
        RefreshChannelStates();
    }

    private void CropPreparedChannelsToResultSelection(int sourceWidth, int sourceHeight, ImageSelectionRect rect)
    {
        if (RedSlot.Image is not { } red ||
            GreenSlot.Image is not { } green ||
            BlueSlot.Image is not { } blue ||
            red.Width != sourceWidth ||
            green.Width != sourceWidth ||
            blue.Width != sourceWidth ||
            red.Height != sourceHeight ||
            green.Height != sourceHeight ||
            blue.Height != sourceHeight)
        {
            SetLastAligned(null);
            return;
        }

        var cropInfo = new CropInfo(
            rect.X,
            rect.Y,
            rect.X + rect.Width,
            rect.Y + rect.Height,
            rect.X,
            rect.Y,
            rect.X + rect.Width,
            rect.Y + rect.Height);

        RedSlot.Image = red.Crop(rect.X, rect.Y, rect.Width, rect.Height);
        GreenSlot.Image = green.Crop(rect.X, rect.Y, rect.Width, rect.Height);
        BlueSlot.Image = blue.Crop(rect.X, rect.Y, rect.Width, rect.Height);
        if (lastAligned is { } aligned &&
            aligned.Red.Width == sourceWidth &&
            aligned.Red.Height == sourceHeight)
        {
            SetLastAligned(AlignedChannelCropper.Crop(aligned, cropInfo));
        }
        else
        {
            SetLastAligned(null);
        }
    }

    private bool TryGetSelectedAutoCleanInputs(
        out ChannelName channelName,
        out ImageBuffer target,
        out ImageBuffer other1,
        out ImageBuffer other2)
    {
        channelName = default;
        target = null!;
        other1 = null!;
        other2 = null!;

        if (SelectedSlot is not { Image: { } selectedImage, ChannelName: { } selectedChannel } ||
            lastAligned is null ||
            !CanReplacePreparedChannel(lastAligned, selectedChannel, selectedImage) ||
            !TryGetWorkingChannels(out var red, out var green, out var blue))
        {
            return false;
        }

        channelName = selectedChannel;
        target = selectedChannel switch
        {
            ChannelName.Red => red,
            ChannelName.Green => green,
            ChannelName.Blue => blue,
            _ => throw new ArgumentOutOfRangeException(nameof(selectedChannel), selectedChannel, null),
        };
        (other1, other2) = selectedChannel switch
        {
            ChannelName.Red => (green, blue),
            ChannelName.Green => (red, blue),
            ChannelName.Blue => (red, green),
            _ => throw new ArgumentOutOfRangeException(nameof(selectedChannel), selectedChannel, null),
        };
        return true;
    }

    private bool TryGetWorkingChannels(out ImageBuffer red, out ImageBuffer green, out ImageBuffer blue)
    {
        red = null!;
        green = null!;
        blue = null!;

        if (RedSlot.Image is not { } r ||
            GreenSlot.Image is not { } g ||
            BlueSlot.Image is not { } b ||
            r.Width != g.Width ||
            r.Width != b.Width ||
            r.Height != g.Height ||
            r.Height != b.Height)
        {
            return false;
        }

        red = r;
        green = g;
        blue = b;
        return true;
    }

    private HealOptions CreateHealOptions()
    {
        return new HealOptions(
            Mode: UseCrossChannelHealing ? HealingMode.CrossChannelGuided : HealingMode.CurrentChannelOnly,
            SubMode: UseTeleaHealing ? HealingSubMode.Telea : HealingSubMode.Patch,
            PatchRadius: HealPatchRadius,
            SearchRadius: HealSearchRadius,
            SafetyRadius: HealSafetyRadius,
            ContextRadius: HealContextRadius,
            MinTrainingPixels: HealMinTrainingPixels,
            UseLocalLinearPrediction: UseLocalLinearPrediction,
            UseGuidedPatchSearch: UseGuidedPatchSearch,
            UseRobustFit: UseRobustFit,
            PredictionAlphaMin: (float)HealPredictionAlphaMin,
            PredictionAlphaMax: (float)HealPredictionAlphaMax,
            FeatherSigma: (float)HealFeatherSigma,
            MaxAllowedErrorFloat: (float)HealMaxAllowedError,
            MaxComponentArea: HealMaxComponentArea,
            LargeComponentConservativeScale: (float)HealLargeComponentConservativeScale,
            QualityMode: AutoCleanQualityMode,
            DebugOutput: DebugHealOutput,
            Diagnostics: CreateDiagnostics());
    }

    private (AutoCleanSettings Detect, HealOptions Apply) CreateAutoCleanResolvedSettings(ChannelName channelName)
    {
        var (detect, apply) = AutoCleanQualityProfiles.Resolve(
            AutoCleanQualityMode,
            CreateAutoCleanSettings(channelName),
            CreateHealOptions());
        return (
            detect with { SessionCache = autoCleanSessionCache },
            apply with { SessionCache = autoCleanSessionCache });
    }

    private AutoCleanSettings CreateAutoCleanSettings(ChannelName channelName)
    {
        return new AutoCleanSettings(
            Sensitivity: AutoCleanSensitivity,
            InpaintRadius: AutoCleanRadius,
            AutoExpandHealingAreaPx: AutoExpandHealingAreaPx,
            AutoMergeNearbyDefects: AutoMergeNearbyDefects,
            AutoMergeDistancePx: AutoMergeDistancePx,
            DebugOutput: DebugHealOutput,
            DebugMaskPrefix: $"{ChannelDebugPrefix(channelName)}_",
            Diagnostics: CreateDiagnostics());
    }

    private static string ChannelDebugPrefix(ChannelName channelName)
    {
        return channelName switch
        {
            ChannelName.Red => "R",
            ChannelName.Green => "G",
            ChannelName.Blue => "B",
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };
    }

    private bool TryGetHealingGuides(ChannelName channelName, out ImageBuffer? guide1, out ImageBuffer? guide2)
    {
        guide1 = null;
        guide2 = null;
        if (!UseCrossChannelHealing || !TryGetWorkingChannels(out var red, out var green, out var blue))
        {
            return false;
        }

        (guide1, guide2) = channelName switch
        {
            ChannelName.Red => (green, blue),
            ChannelName.Green => (red, blue),
            ChannelName.Blue => (red, green),
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };
        return true;
    }

    private void BeginAutoCleanMaskReview(ChannelName channelName, byte[] mask)
    {
        PendingAutoCleanChannel = channelName;
        pendingAutoCleanBaseMask = (byte[])mask.Clone();
        pendingAutoCleanAddMask = new byte[mask.Length];
        pendingAutoCleanRemoveMask = new byte[mask.Length];
        RebuildPendingAutoCleanMaskFromLayers();
        ShowAutoCleanResultPreview = false;
        SelectionRect = ImageSelectionRect.Empty;
        RefreshPreviewBindings();
        NotifyAutoCleanCommands();
    }

    private void ClearPendingAutoCleanMask()
    {
        autoCleanMaskRefreshCancellation?.Cancel();
        CancelAutoCleanProgress();
        if (PendingAutoCleanMask is null &&
            PendingAutoCleanChannel is null &&
            AutoCleanMaskOverlayBitmap is null &&
            pendingAutoCleanBaseMask is null &&
            pendingAutoCleanAddMask is null &&
            pendingAutoCleanRemoveMask is null)
        {
            return;
        }

        pendingAutoCleanBaseMask = null;
        pendingAutoCleanAddMask = null;
        pendingAutoCleanRemoveMask = null;
        PendingAutoCleanMask = null;
        PendingAutoCleanChannel = null;
        PendingAutoCleanCandidatePixels = 0;
        ShowAutoCleanResultPreview = false;
        AutoCleanMaskOverlayBitmap = null;
        RefreshPreviewBindings();
        NotifyAutoCleanCommands();
    }

    private int BeginAutoCleanProgress()
    {
        var version = ++autoCleanProgressVersion;
        AutoCleanProgress = 0;
        IsAutoCleanProgressVisible = true;
        return version;
    }

    private IProgress<double> CreateAutoCleanProgress(int version)
    {
        return new Progress<double>(value =>
        {
            if (version != autoCleanProgressVersion)
            {
                return;
            }

            AutoCleanProgress = Math.Clamp(value, 0.0, 100.0);
        });
    }

    private void EndAutoCleanProgress(int version)
    {
        if (version != autoCleanProgressVersion)
        {
            return;
        }

        IsAutoCleanProgressVisible = false;
        AutoCleanProgress = 0;
    }

    private void CancelAutoCleanProgress()
    {
        autoCleanProgressVersion++;
        IsAutoCleanProgressVisible = false;
        AutoCleanProgress = 0;
    }

    private void RefreshAutoCleanMaskOverlay()
    {
        if (!ShowHealMaskOverlay ||
            PendingAutoCleanMask is not { } mask ||
            SelectedSlot?.Image is not { } image ||
            mask.Length != image.Width * image.Height)
        {
            AutoCleanMaskOverlayBitmap = null;
            return;
        }

        AutoCleanMaskOverlayBitmap = AvaloniaBitmapFactory.FromMaskOverlay(mask, image.Width, image.Height);
    }

    private void RefreshChannelStates()
    {
        RedSlot.State = ComputeChannelState(RedSlot);
        GreenSlot.State = ComputeChannelState(GreenSlot);
        BlueSlot.State = ComputeChannelState(BlueSlot);
        ResultSlot.State = ResultSlot.HasImage ? ChannelSlotState.Result : ChannelSlotState.Empty;
        OnPropertyChanged(nameof(InspectorStateLabel));
    }

    private ChannelSlotState ComputeChannelState(ChannelSlotViewModel slot)
    {
        if (!slot.HasImage)
        {
            return ChannelSlotState.Empty;
        }

        if (slot.IsResultSlot)
        {
            return ChannelSlotState.Result;
        }

        if (lastAligned is null || slot.ChannelName is not { } channelName || slot.Image is not { } current)
        {
            return ChannelSlotState.Loaded;
        }

        var prepared = channelName switch
        {
            ChannelName.Red => lastAligned.Red,
            ChannelName.Green => lastAligned.Green,
            ChannelName.Blue => lastAligned.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };

        if (ReferenceEquals(current, prepared))
        {
            return ChannelSlotState.Aligned;
        }

        if (current.Width == prepared.Width && current.Height == prepared.Height)
        {
            return ChannelSlotState.Retouched;
        }

        return ChannelSlotState.Loaded;
    }

    private string FormatAlignSummary(ChannelName channelName)
    {
        if (lastAligned is null ||
            !lastAligned.AlignMetadata.TryGetValue(channelName, out var info))
        {
            return $"{FormatAlignSummaryLabel(channelName)}: —";
        }

        if (info.Shifts is null or { Count: 0 })
        {
            return $"{FormatAlignSummaryLabel(channelName)}: {info.Kind} ({info.Inliers} inliers)";
        }

        var shiftText = string.Join(
            ", ",
            info.Shifts.Select(shift => $"({shift.Dx:F1},{shift.Dy:F1})"));
        return $"{FormatAlignSummaryLabel(channelName)}: {info.Kind} ({info.Inliers} inliers, shifts {shiftText})";
    }

    private static string FormatAlignSummaryLabel(ChannelName channelName) =>
        channelName switch
        {
            ChannelName.Red => "Red",
            ChannelName.Green => "Green",
            ChannelName.Blue => "Blue",
            _ => channelName.ToString(),
        };

    private static void TryOpenExportFolder(string path)
    {
        try
        {
            var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
        }
    }

    private void SchedulePendingAutoCleanMaskRefresh()
    {
        if (!IsAutoCleanMaskPending)
        {
            return;
        }

        autoCleanMaskRefreshCancellation?.Cancel();
        autoCleanMaskRefreshCancellation = new CancellationTokenSource();
        var token = autoCleanMaskRefreshCancellation.Token;
        _ = RefreshPendingAutoCleanMaskAfterDelay(token);
    }

    private async Task RefreshPendingAutoCleanMaskAfterDelay(CancellationToken cancellationToken)
    {
        var progressScope = 0;
        try
        {
            await Task.Delay(180, cancellationToken);
            if (!TryGetSelectedAutoCleanInputs(out var channelName, out var target, out var other1, out var other2) ||
                PendingAutoCleanChannel != channelName)
            {
                return;
            }

            progressScope = BeginAutoCleanProgress();
            var progress = CreateAutoCleanProgress(progressScope);
            var (settings, _) = CreateAutoCleanResolvedSettings(channelName);
            var result = await Task.Run(
                () => ChannelRetoucher.DetectSingleChannelDefects(target, other1, other2, settings, progress),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                PendingAutoCleanChannel != channelName)
            {
                return;
            }

            pendingAutoCleanBaseMask = result.Mask;
            EnsureAutoCleanEditLayers(result.Mask.Length);
            RebuildPendingAutoCleanMaskFromLayers();
            Status = $"Review auto-clean mask for {SelectedSlot!.DisplayName}: {PendingAutoCleanCandidatePixels} candidate pixels.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            if (progressScope != 0)
            {
                EndAutoCleanProgress(progressScope);
            }
        }
    }

    private void RebuildPendingAutoCleanMaskFromLayers()
    {
        if (pendingAutoCleanBaseMask is not { } baseMask)
        {
            PendingAutoCleanMask = null;
            PendingAutoCleanCandidatePixels = 0;
            return;
        }

        EnsureAutoCleanEditLayers(baseMask.Length);
        var addMask = pendingAutoCleanAddMask!;
        var removeMask = pendingAutoCleanRemoveMask!;
        var composite = new byte[baseMask.Length];
        var count = 0;
        for (var i = 0; i < composite.Length; i++)
        {
            if ((baseMask[i] > 0 || addMask[i] > 0) && removeMask[i] == 0)
            {
                composite[i] = 1;
                count++;
            }
        }

        PendingAutoCleanMask = composite;
        PendingAutoCleanCandidatePixels = count;
    }

    private void EnsureAutoCleanEditLayers(int length)
    {
        if (pendingAutoCleanAddMask?.Length != length)
        {
            pendingAutoCleanAddMask = new byte[length];
        }

        if (pendingAutoCleanRemoveMask?.Length != length)
        {
            pendingAutoCleanRemoveMask = new byte[length];
        }
    }

    private void ApplyManualAutoCleanMaskEdit(byte[] editMask, AutoCleanMaskEditAction action)
    {
        if (pendingAutoCleanAddMask is null || pendingAutoCleanRemoveMask is null)
        {
            return;
        }

        for (var i = 0; i < editMask.Length; i++)
        {
            if (editMask[i] == 0)
            {
                continue;
            }

            if (action == AutoCleanMaskEditAction.Add)
            {
                pendingAutoCleanAddMask[i] = 1;
                pendingAutoCleanRemoveMask[i] = 0;
            }
            else
            {
                pendingAutoCleanRemoveMask[i] = 1;
                pendingAutoCleanAddMask[i] = 0;
            }
        }
    }

    private static byte[] CreateBrushMaskEdit(
        int width,
        int height,
        AutoCleanMaskEditOperation operation)
    {
        return ChannelRetoucher.CreateBrushMask(
            width,
            height,
            [new RetouchStroke([operation.Start], Math.Clamp(operation.BrushSize, 1, 200))]);
    }

    private static byte[] CreateRectangleMaskEdit(
        int width,
        int height,
        AutoCleanMaskEditOperation operation)
    {
        var mask = new byte[width * height];
        var x0 = Math.Clamp((int)MathF.Round(Math.Min(operation.Start.X, operation.End.X)), 0, width - 1);
        var y0 = Math.Clamp((int)MathF.Round(Math.Min(operation.Start.Y, operation.End.Y)), 0, height - 1);
        var x1 = Math.Clamp((int)MathF.Round(Math.Max(operation.Start.X, operation.End.X)), 0, width - 1);
        var y1 = Math.Clamp((int)MathF.Round(Math.Max(operation.Start.Y, operation.End.Y)), 0, height - 1);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                mask[(y * width) + x] = 1;
            }
        }

        return mask;
    }

    private ChannelSlotViewModel GetSlot(ChannelName channelName)
    {
        return channelName switch
        {
            ChannelName.Red => RedSlot,
            ChannelName.Green => GreenSlot,
            ChannelName.Blue => BlueSlot,
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };
    }

    private static bool CanReplacePreparedChannel(AlignedChannels aligned, ChannelName channelName, ImageBuffer image)
    {
        var current = channelName switch
        {
            ChannelName.Red => aligned.Red,
            ChannelName.Green => aligned.Green,
            ChannelName.Blue => aligned.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };

        return current.Width == image.Width && current.Height == image.Height;
    }

    private static AlignedChannels ReplaceAlignedChannel(AlignedChannels aligned, ChannelName channelName, ImageBuffer image)
    {
        var mask = FullMask(image);
        return channelName switch
        {
            ChannelName.Red => aligned with { Red = image, MaskRed = mask },
            ChannelName.Green => aligned with { Green = image, MaskGreen = mask },
            ChannelName.Blue => aligned with { Blue = image, MaskBlue = mask },
            _ => throw new ArgumentOutOfRangeException(nameof(channelName), channelName, null),
        };
    }

    private PipelineSettings CurrentPipelineSettings(bool skipCrop = false)
    {
        return new PipelineSettings
        {
            Align = new AlignOptions(
                Reference: AlignReference,
                Detector: AlignDetector,
                MaxTranslation: AlignMaxTranslation,
                MaxFineIterations: AlignMaxFineIterations,
                TrimBorders: TrimDarkBorders,
                CoarseAlignmentMaxSide: AlignCoarseMaxSide),
            Color = new ColorSettings(
                Source: WhiteBalanceSource,
                Temperature: ColorTemperature,
                Tint: ColorTint,
                WhitePick: HasPipetteWhiteBalance
                    ? new WhitePick(whiteBalancePipetteX, whiteBalancePipetteY, WhitePickRadius)
                    : null),
            Exposure = new ChannelExposureSettings(
                (float)RedExposureStops,
                (float)GreenExposureStops,
                (float)BlueExposureStops),
            Levels = new LevelsSettings(
                Mode: LevelsMode,
                BlackPoint: (float)LevelsBlackPoint,
                WhitePoint: (float)LevelsWhitePoint,
                Gamma: (float)LevelsGamma),
            ChannelLevels = new ChannelLevelsSettings(
                Red: new ChannelLevelSettings((float)RedLevelsBlackPoint, (float)RedLevelsWhitePoint, (float)RedLevelsGamma),
                Green: new ChannelLevelSettings((float)GreenLevelsBlackPoint, (float)GreenLevelsWhitePoint, (float)GreenLevelsGamma),
                Blue: new ChannelLevelSettings((float)BlueLevelsBlackPoint, (float)BlueLevelsWhitePoint, (float)BlueLevelsGamma)),
            Crop = new CropSettings { SkipCrop = skipCrop },
            Diagnostics = CreateDiagnostics(),
        };
    }

    private Core.Diagnostics.IProcessingDiagnostics CreateDiagnostics() =>
        new GuiProcessingDiagnostics(AppendLog, CurrentDiagnosticsSettings().ToOptions());

    private ProcessingDiagnosticsSettings CurrentDiagnosticsSettings() =>
        new(
            LogComputeBackends,
            LogPipelineStages,
            LogCpuParallel,
            LogTimings);

    private void LoadDiagnosticsSettings(ProcessingDiagnosticsSettings settings)
    {
        suppressDiagnosticsSettingsSave = true;
        try
        {
            LogComputeBackends = settings.LogComputeBackends;
            LogPipelineStages = settings.LogPipelineStages;
            LogCpuParallel = settings.LogCpuParallel;
            LogTimings = settings.LogTimings;
        }
        finally
        {
            suppressDiagnosticsSettingsSave = false;
        }
    }

    private void LoadAutoCleanSettings(AutoCleanSettingsSnapshot settings)
    {
        suppressAutoCleanSettingsSave = true;
        try
        {
            AutoCleanQualityMode = settings.QualityMode;
            AutoCleanSensitivity = settings.Sensitivity;
            AutoCleanRadius = settings.InpaintRadius;
            HealPatchRadius = settings.PatchRadius;
            HealSearchRadius = settings.SearchRadius;
            HealSafetyRadius = settings.SafetyRadius;
            HealContextRadius = settings.ContextRadius;
            HealMinTrainingPixels = settings.MinTrainingPixels;
            UseCrossChannelHealing = settings.UseCrossChannelHealing;
            UseTeleaHealing = settings.UseTeleaHealing;
            UseLocalLinearPrediction = settings.UseLocalLinearPrediction;
            UseGuidedPatchSearch = settings.UseGuidedPatchSearch;
            UseRobustFit = settings.UseRobustFit;
            AutoMergeNearbyDefects = settings.AutoMergeNearbyDefects;
            AutoMergeDistancePx = settings.AutoMergeDistancePx;
            AutoExpandHealingAreaPx = settings.AutoExpandHealingAreaPx;
            HealMaxComponentArea = settings.MaxComponentArea;
            HealPredictionAlphaMin = settings.PredictionAlphaMin;
            HealPredictionAlphaMax = settings.PredictionAlphaMax;
            HealFeatherSigma = settings.FeatherSigma;
            HealMaxAllowedError = settings.MaxAllowedError;
            HealLargeComponentConservativeScale = settings.LargeComponentConservativeScale;
            DebugHealOutput = settings.DebugHealOutput;
            ShowHealMaskOverlay = settings.ShowHealMaskOverlay;
        }
        finally
        {
            suppressAutoCleanSettingsSave = false;
        }
    }

    private void SaveAutoCleanSettings()
    {
        if (suppressAutoCleanSettingsSave)
        {
            return;
        }

        try
        {
            autoCleanSettingsStore.Save(new AutoCleanSettingsSnapshot(
                QualityMode: AutoCleanQualityMode,
                Sensitivity: AutoCleanSensitivity,
                InpaintRadius: AutoCleanRadius,
                PatchRadius: HealPatchRadius,
                SearchRadius: HealSearchRadius,
                SafetyRadius: HealSafetyRadius,
                ContextRadius: HealContextRadius,
                MinTrainingPixels: HealMinTrainingPixels,
                UseCrossChannelHealing: UseCrossChannelHealing,
                UseTeleaHealing: UseTeleaHealing,
                UseLocalLinearPrediction: UseLocalLinearPrediction,
                UseGuidedPatchSearch: UseGuidedPatchSearch,
                UseRobustFit: UseRobustFit,
                AutoMergeNearbyDefects: AutoMergeNearbyDefects,
                AutoMergeDistancePx: AutoMergeDistancePx,
                AutoExpandHealingAreaPx: AutoExpandHealingAreaPx,
                MaxComponentArea: HealMaxComponentArea,
                PredictionAlphaMin: (float)HealPredictionAlphaMin,
                PredictionAlphaMax: (float)HealPredictionAlphaMax,
                FeatherSigma: (float)HealFeatherSigma,
                MaxAllowedError: (float)HealMaxAllowedError,
                LargeComponentConservativeScale: (float)HealLargeComponentConservativeScale,
                DebugHealOutput: DebugHealOutput,
                ShowHealMaskOverlay: ShowHealMaskOverlay));
        }
        catch (IOException ex)
        {
            AppendLog($"Failed to save auto-clean settings: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"Failed to save auto-clean settings: {ex.Message}");
        }
    }

    private void LoadUiSettings(UiSettings settings)
    {
        settings = settings.Normalize();
        suppressUiSettingsSave = true;
        try
        {
            AppThemeMode = settings.ThemeMode;
            LeftPanelWidth = settings.LeftPanelWidth;
            RightInspectorWidth = settings.RightInspectorWidth;
            ProcessingLogHeight = settings.ProcessingLogHeight;
            IsProcessingLogVisible = settings.IsProcessingLogVisible;
            IsRightInspectorVisible = settings.IsRightInspectorVisible;
            IsLeftPanelVisible = settings.IsLeftPanelVisible;
            SelectedWorkflowTool = settings.SelectedWorkflowTool;
            IsLoupeEnabled = settings.IsLoupeEnabled;
            ThemeService.Apply(AppThemeMode);
            NotifyThemeMenuSelection();
        }
        finally
        {
            suppressUiSettingsSave = false;
        }
    }

    public void SaveUiSettings()
    {
        if (suppressUiSettingsSave)
        {
            return;
        }

        try
        {
            uiSettingsStore.Save(CurrentUiSettings());
        }
        catch (IOException ex)
        {
            AppendLog($"Failed to save UI settings: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"Failed to save UI settings: {ex.Message}");
        }
    }

    public bool PreviewLoupeEnabled => IsLoupeEnabled && PreviewHasImage;

    private UiSettings CurrentUiSettings() =>
        new UiSettings
        {
            ThemeMode = AppThemeMode,
            LeftPanelWidth = LeftPanelWidth,
            RightInspectorWidth = RightInspectorWidth,
            ProcessingLogHeight = ProcessingLogHeight,
            IsProcessingLogVisible = IsProcessingLogVisible,
            IsRightInspectorVisible = IsRightInspectorVisible,
            IsLeftPanelVisible = IsLeftPanelVisible,
            SelectedWorkflowTool = SelectedWorkflowTool,
            IsLoupeEnabled = IsLoupeEnabled,
        }.Normalize();

    private void NotifyThemeMenuSelection()
    {
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsSystemThemeSelected));
    }

    private void SaveDiagnosticsSettings()
    {
        if (suppressDiagnosticsSettingsSave)
        {
            return;
        }

        try
        {
            diagnosticsSettingsStore.Save(CurrentDiagnosticsSettings());
        }
        catch (IOException ex)
        {
            AppendLog($"Could not save diagnostics settings: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"Could not save diagnostics settings: {ex.Message}");
        }
    }

    partial void OnLogComputeBackendsChanged(bool value) => SaveDiagnosticsSettings();

    partial void OnLogPipelineStagesChanged(bool value) => SaveDiagnosticsSettings();

    partial void OnLogCpuParallelChanged(bool value) => SaveDiagnosticsSettings();

    partial void OnLogTimingsChanged(bool value) => SaveDiagnosticsSettings();

    private bool HasPipetteWhiteBalance => whiteBalancePipetteX >= 0 && whiteBalancePipetteY >= 0;

    public int WhitePickX => whiteBalancePipetteX;

    public int WhitePickY => whiteBalancePipetteY;

    public bool ShowWhitePick => HasPipetteWhiteBalance && SelectedSlot == ResultSlot;

    private void RefreshAlignedAfterInputEdit(ChannelName channelName)
    {
        if (lastAligned is not { } aligned)
        {
            ClearAlignedAfterInputEdit();
            return;
        }

        var slot = GetSlot(channelName);
        if (slot.Image is not { } image)
        {
            ClearAlignedAfterInputEdit();
            return;
        }

        if (CanReplacePreparedChannel(aligned, channelName, image))
        {
            SetLastAligned(ReplaceAlignedChannel(aligned, channelName, image));
            ScheduleResultRebuild();
            return;
        }

        if (aligned.AlignTransforms is not { } transforms ||
            !transforms.TryGetValue(channelName, out var transform) ||
            !transform.CanApplyTo(image))
        {
            ClearAlignedAfterInputEdit();
            return;
        }

        var transformed = ChannelAligner.ApplyTransform(image, transform);
        SetLastAligned(channelName switch
        {
            ChannelName.Red => aligned with { Red = transformed.Image, MaskRed = transformed.Mask },
            ChannelName.Green => aligned with { Green = transformed.Image, MaskGreen = transformed.Mask },
            ChannelName.Blue => aligned with { Blue = transformed.Image, MaskBlue = transformed.Mask },
            _ => aligned,
        });

        ScheduleResultRebuild();
    }

    private void ClearAlignedAfterInputEdit()
    {
        SetLastAligned(null);
        ResultSlot.Result = null;
        Status = "Input channel changed. Run Auto-align again.";
    }

    private void ScheduleResultRebuild()
    {
        if (isRestoringSnapshot || lastAligned is null)
        {
            return;
        }

        resultRebuildCancellation?.Cancel();
        resultRebuildCancellation = new CancellationTokenSource();
        var token = resultRebuildCancellation.Token;
        _ = RebuildResultAfterDelay(token);
    }

    private async Task RebuildResultAfterDelay(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(160, cancellationToken);
            var aligned = EditorSession.CloneAligned(lastAligned);
            if (aligned is null)
            {
                return;
            }

            var settings = CurrentPipelineSettings(skipCrop: true);
            var manual = CurrentManualNudges();
            var result = await Task.Run(
                () => ReconstructionPipeline.BuildRgb(aligned, settings, manual.Count > 0 ? manual : null),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ResultSlot.ApplyEditedResult(result.Rgb);
            RefreshPreviewBindings();
            RefreshChannelStates();
            AppendLog($"Result rebuilt from cached alignment: {result.Rgb.Width}x{result.Rgb.Height}.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            AppendLog($"Error: {ex.Message}");
        }
    }

    private bool CanExport()
    {
        return !IsBusy && ResultSlot.Result is not null;
    }

    private bool CanExportChannels()
    {
        return !IsBusy && RedSlot.Image is not null && GreenSlot.Image is not null && BlueSlot.Image is not null;
    }

    private void LoadExportSettings(RgbExportSettings settings)
    {
        settings = settings.Normalize();
        suppressExportSettingsSave = true;
        try
        {
            ExportFormat = settings.Format;
            LimitExportSize = settings.MaxSide.HasValue;
            ExportMaxSide = settings.MaxSide ?? 2048;
            PngCompression = settings.PngCompression;
            JpegQuality = settings.JpegQuality;
            TiffCompression = settings.TiffCompression;
            TiffDeflateLevel = settings.TiffDeflateLevel;
        }
        finally
        {
            suppressExportSettingsSave = false;
        }
    }

    private RgbExportSettings CurrentExportSettings()
    {
        return (RgbExportSettings.Default with
        {
            Format = ExportFormat,
            MaxSide = LimitExportSize ? ExportMaxSide : null,
            PngCompression = PngCompression,
            JpegQuality = JpegQuality,
            TiffCompression = TiffCompression,
            TiffDeflateLevel = TiffDeflateLevel,
        }).Normalize();
    }

    private void SaveExportSettings()
    {
        if (suppressExportSettingsSave)
        {
            return;
        }

        try
        {
            exportSettingsStore.Save(CurrentExportSettings());
        }
        catch (IOException ex)
        {
            AppendLog($"Could not save export settings: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"Could not save export settings: {ex.Message}");
        }
    }

    partial void OnSelectedSlotChanged(ChannelSlotViewModel? value)
    {
        OnPropertyChanged(nameof(ShowWhitePick));
        if (ToolMode == EditorToolMode.WhiteBalancePicker && value != ResultSlot)
        {
            ToolMode = EditorToolMode.Select;
        }

        SelectionRect = ImageSelectionRect.Empty;
        ClearPendingAutoCleanMask();
        AutoCleanSelectedChannelCommand.NotifyCanExecuteChanged();
        ApplyRetouchStrokeCommand.NotifyCanExecuteChanged();
        ApplyStampStrokeCommand.NotifyCanExecuteChanged();
        PickWhiteBalanceCommand.NotifyCanExecuteChanged();
        RefreshPreviewBindings();
        OnPropertyChanged(nameof(InspectorChannelLabel));
        OnPropertyChanged(nameof(InspectorSizeLabel));
        OnPropertyChanged(nameof(InspectorStateLabel));
        OnPropertyChanged(nameof(SelectedSlotSourceBitDepth));
        OnPropertyChanged(nameof(CropBehaviorHint));
        OnPropertyChanged(nameof(InputModeLabel));
    }

    partial void OnSelectionRectChanged(ImageSelectionRect value)
    {
        CropToSelectionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionX));
        OnPropertyChanged(nameof(SelectionY));
        OnPropertyChanged(nameof(SelectionWidth));
        OnPropertyChanged(nameof(SelectionHeight));
        MarkProjectDirty();
    }

    partial void OnToolModeChanged(EditorToolMode value)
    {
        SelectionRect = ImageSelectionRect.Empty;
        OnPropertyChanged(nameof(IsSelectToolMode));
        OnPropertyChanged(nameof(IsHealToolMode));
        OnPropertyChanged(nameof(IsCloneToolMode));
        OnPropertyChanged(nameof(IsWhiteBalancePickerToolMode));
        MarkProjectDirty();
        NotifyKeyboardShortcutCommandsChanged();
    }

    partial void OnAppThemeModeChanged(AppThemeMode value)
    {
        ThemeService.Apply(value);
        NotifyThemeMenuSelection();
        SaveUiSettings();
    }

    partial void OnIsLeftPanelVisibleChanged(bool value) => SaveUiSettings();

    partial void OnIsRightInspectorVisibleChanged(bool value) => SaveUiSettings();

    partial void OnIsProcessingLogVisibleChanged(bool value) => SaveUiSettings();

    partial void OnSelectedWorkflowToolChanged(WorkflowTool value)
    {
        SaveUiSettings();
        MarkProjectDirty();
        NotifyKeyboardShortcutCommandsChanged();
    }

    partial void OnLeftPanelWidthChanged(double value) => SaveUiSettings();

    partial void OnRightInspectorWidthChanged(double value) => SaveUiSettings();

    partial void OnProcessingLogHeightChanged(double value) => SaveUiSettings();

    partial void OnAutoCleanQualityModeChanged(AutoCleanQualityMode value)
    {
        SaveAutoCleanSettings();
        SchedulePendingAutoCleanMaskRefresh();
        MarkProjectDirty();
    }

    partial void OnShowHealMaskOverlayChanged(bool value)
    {
        SaveAutoCleanSettings();
        RefreshAutoCleanMaskOverlay();
        MarkProjectDirty();
    }

    partial void OnDebugHealOutputChanged(bool value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnUseCrossChannelHealingChanged(bool value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnUseTeleaHealingChanged(bool value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnUseLocalLinearPredictionChanged(bool value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnUseGuidedPatchSearchChanged(bool value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnUseRobustFitChanged(bool value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealPatchRadiusChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealSearchRadiusChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealSafetyRadiusChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealContextRadiusChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealMinTrainingPixelsChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealMaxComponentAreaChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealPredictionAlphaMinChanged(double value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealPredictionAlphaMaxChanged(double value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealFeatherSigmaChanged(double value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealMaxAllowedErrorChanged(double value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnHealLargeComponentConservativeScaleChanged(double value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnAutoCleanRadiusChanged(int value)
    {
        SaveAutoCleanSettings();
        MarkProjectDirty();
    }

    partial void OnAutoCleanSensitivityChanged(int value)
    {
        SaveAutoCleanSettings();
        SchedulePendingAutoCleanMaskRefresh();
        MarkProjectDirty();
    }

    partial void OnAutoExpandHealingAreaPxChanged(int value)
    {
        SaveAutoCleanSettings();
        SchedulePendingAutoCleanMaskRefresh();
        MarkProjectDirty();
    }

    partial void OnAutoMergeNearbyDefectsChanged(bool value)
    {
        SaveAutoCleanSettings();
        SchedulePendingAutoCleanMaskRefresh();
        MarkProjectDirty();
    }

    partial void OnAutoMergeDistancePxChanged(int value)
    {
        SaveAutoCleanSettings();
        SchedulePendingAutoCleanMaskRefresh();
        MarkProjectDirty();
    }

    partial void OnPendingAutoCleanMaskChanged(byte[]? value)
    {
        RefreshAutoCleanMaskOverlay();
        RefreshPreviewBindings();
        NotifyAutoCleanCommands();
        NotifyKeyboardShortcutCommandsChanged();
    }

    partial void OnPendingAutoCleanChannelChanged(ChannelName? value)
    {
        RefreshPreviewBindings();
        NotifyAutoCleanCommands();
        NotifyKeyboardShortcutCommandsChanged();
    }

    partial void OnShowAutoCleanResultPreviewChanged(bool value)
    {
        RefreshPreviewBindings();
    }

    partial void OnExportFormatChanged(RgbExportFormat value)
    {
        SaveExportSettings();
        MarkProjectDirty();
    }

    partial void OnLimitExportSizeChanged(bool value)
    {
        SaveExportSettings();
        MarkProjectDirty();
    }

    partial void OnExportMaxSideChanged(int value)
    {
        SaveExportSettings();
        MarkProjectDirty();
    }

    partial void OnPngCompressionChanged(int value)
    {
        SaveExportSettings();
        MarkProjectDirty();
    }

    partial void OnJpegQualityChanged(int value)
    {
        SaveExportSettings();
        MarkProjectDirty();
    }

    partial void OnTiffCompressionChanged(TiffExportCompression value)
    {
        OnPropertyChanged(nameof(IsTiffDeflate));
        SaveExportSettings();
        MarkProjectDirty();
    }

    partial void OnTiffDeflateLevelChanged(int value)
    {
        SaveExportSettings();
        MarkProjectDirty();
    }

    private static byte[] FullMask(ImageBuffer image)
    {
        var mask = new byte[image.Width * image.Height];
        Array.Fill(mask, (byte)1);
        return mask;
    }

    private void RefreshPreviewImageContext()
    {
        previewImageContextVersion++;
        RefreshPreviewBindings();
        OnPropertyChanged(nameof(PreviewImageContextKey));
    }

    private void RefreshPreviewBindings()
    {
        OnPropertyChanged(nameof(PreviewDisplayBitmap));
        OnPropertyChanged(nameof(PreviewHasImage));
        OnPropertyChanged(nameof(PreviewLoupeEnabled));
        OnPropertyChanged(nameof(PreviewInteractionMode));
        OnPropertyChanged(nameof(CanUseWhiteBalancePicker));
        PickWhiteBalanceCommand.NotifyCanExecuteChanged();
        ToggleLoupeShortcutCommand.NotifyCanExecuteChanged();
    }

    private void NotifyAutoCleanCommands()
    {
        AutoCleanSelectedChannelCommand.NotifyCanExecuteChanged();
        ApplyAutoCleanMaskCommand.NotifyCanExecuteChanged();
        CancelAutoCleanMaskCommand.NotifyCanExecuteChanged();
        EditAutoCleanMaskCommand.NotifyCanExecuteChanged();
        CropToSelectionCommand.NotifyCanExecuteChanged();
        ApplyRetouchStrokeCommand.NotifyCanExecuteChanged();
        ApplyStampStrokeCommand.NotifyCanExecuteChanged();
        PickWhiteBalanceCommand.NotifyCanExecuteChanged();
    }

    private void AppendLog(string message)
    {
        lock (processingLogSync)
        {
            processingLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            TrimProcessingLogIfNeeded();
        }

        ScheduleProcessingLogRefresh();
    }

    private void TrimProcessingLogIfNeeded()
    {
        if (processingLog.Length <= MaxProcessingLogCharacters)
        {
            return;
        }

        var dropLength = processingLog.Length - (MaxProcessingLogCharacters * 2 / 3);
        var remainder = processingLog.ToString(dropLength, processingLog.Length - dropLength);
        var firstLineBreak = remainder.IndexOf('\n');
        if (firstLineBreak < 0)
        {
            processingLog.Clear();
            return;
        }

        processingLog.Remove(0, dropLength + firstLineBreak + 1);
    }

    private void ScheduleProcessingLogRefresh()
    {
        if (processingLogRefreshScheduled)
        {
            return;
        }

        processingLogRefreshScheduled = true;
        Dispatcher.UIThread.Post(NotifyProcessingLogChanged, DispatcherPriority.Background);
    }

    private void NotifyProcessingLogChanged()
    {
        processingLogRefreshScheduled = false;
        OnPropertyChanged(nameof(ProcessingLogText));
    }

    private static string FormatCropInfo(CropInfo cropInfo)
    {
        var cropWidth = cropInfo.X1 - cropInfo.X0;
        var cropHeight = cropInfo.Y1 - cropInfo.Y0;
        return $"Auto-crop: ({cropInfo.X0},{cropInfo.Y0})-({cropInfo.X1},{cropInfo.Y1}) => {cropWidth}x{cropHeight}; overlap ({cropInfo.OverlapX0},{cropInfo.OverlapY0})-({cropInfo.OverlapX1},{cropInfo.OverlapY1}).";
    }
}
