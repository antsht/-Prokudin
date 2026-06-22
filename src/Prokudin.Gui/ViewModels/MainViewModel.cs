using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Imaging;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int UndoLimit = 20;
    private const int WhiteBalancePipetteRadius = 3;
    private readonly IFileDialogService fileDialogService;
    private readonly IExportSettingsStore exportSettingsStore;
    private readonly StringBuilder processingLog = new();
    private readonly List<EditorSnapshot> undoHistory = [];
    private readonly List<EditorSnapshot> redoHistory = [];
    private AlignedChannels? lastAligned;
    private CancellationTokenSource? resultRebuildCancellation;
    private bool isRestoringSnapshot;
    private bool suppressUndoCapture;
    private bool exposureUndoOpen;
    private bool suppressExportSettingsSave;
    private int exposureChangeVersion;
    private int previewImageContextVersion;
    private Bitmap? autoCleanMaskOverlayBitmap;
    private byte[]? pendingAutoCleanBaseMask;
    private byte[]? pendingAutoCleanAddMask;
    private byte[]? pendingAutoCleanRemoveMask;
    private CancellationTokenSource? autoCleanMaskRefreshCancellation;
    private int autoCleanProgressVersion;
    private int whiteBalancePipetteX = -1;
    private int whiteBalancePipetteY = -1;

    public MainViewModel(IFileDialogService fileDialogService)
        : this(fileDialogService, new JsonExportSettingsStore())
    {
    }

    public MainViewModel(IFileDialogService fileDialogService, IExportSettingsStore exportSettingsStore)
    {
        this.fileDialogService = fileDialogService;
        this.exportSettingsStore = exportSettingsStore;

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
        SelectedSlot = RedSlot;
    }

    public ChannelSlotViewModel RedSlot { get; }

    public ChannelSlotViewModel GreenSlot { get; }

    public ChannelSlotViewModel BlueSlot { get; }

    public ChannelSlotViewModel ResultSlot { get; }

    public ObservableCollection<ChannelSlotViewModel> Slots { get; }

    public IReadOnlyList<string> TriptychOrders { get; } = ["RGB", "BGR"];

    public IReadOnlyList<RgbExportFormat> ExportFormats { get; } =
        [RgbExportFormat.Png, RgbExportFormat.Jpeg, RgbExportFormat.Tiff];

    public IReadOnlyList<TiffExportCompression> TiffCompressions { get; } =
        [TiffExportCompression.None, TiffExportCompression.Lzw, TiffExportCompression.Deflate];

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
    [NotifyPropertyChangedFor(nameof(CanUseWhiteBalancePicker))]
    private bool isBusy;

    [ObservableProperty]
    private string status = "Open three channels or a triptych to begin.";

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
    private bool autoWhiteBalance = true;

    [ObservableProperty]
    private double redExposureStops;

    [ObservableProperty]
    private double greenExposureStops;

    [ObservableProperty]
    private double blueExposureStops;

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

    public string ProcessingLogText => processingLog.ToString();

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
    private void ToggleFitToWindow()
    {
        PreviewZoomMode = PreviewZoomMode == PreviewZoomMode.OneToOne
            ? PreviewZoomMode.FitToWindow
            : PreviewZoomMode.OneToOne;
        AppendLog($"Preview zoom: {PreviewZoomMode}.");
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!CanUndo())
        {
            return;
        }

        ClearPendingAutoCleanMask();
        CloseExposureUndoWindow();
        var snapshot = undoHistory[^1];
        undoHistory.RemoveAt(undoHistory.Count - 1);
        redoHistory.Add(CaptureSnapshot());
        RestoreSnapshot(snapshot);
        Status = "Undo.";
        AppendLog("Undo.");
        NotifyHistoryCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!CanRedo())
        {
            return;
        }

        ClearPendingAutoCleanMask();
        CloseExposureUndoWindow();
        var snapshot = redoHistory[^1];
        redoHistory.RemoveAt(redoHistory.Count - 1);
        undoHistory.Add(CaptureSnapshot());
        RestoreSnapshot(snapshot);
        Status = "Redo.";
        AppendLog("Redo.");
        NotifyHistoryCommands();
    }

    [RelayCommand(CanExecute = nameof(CanCropToSelection))]
    private void CropToSelection()
    {
        if (SelectedSlot is null || SelectionRect.IsEmpty)
        {
            return;
        }

        PushUndo();
        var rect = SelectionRect;

        if (SelectedSlot.Result is { } rgb)
        {
            ResultSlot.Result = rgb.Crop(rect.X, rect.Y, rect.Width, rect.Height);
            CropPreparedChannelsToResultSelection(rgb.Width, rgb.Height, rect);
            AppendLog($"Cropped result to {rect.X},{rect.Y} {rect.Width}x{rect.Height} -> {ResultSlot.Result.Width}x{ResultSlot.Result.Height}.");
            Status = $"Cropped result to {ResultSlot.Result.Width} x {ResultSlot.Result.Height}.";
        }
        else if (SelectedSlot.Image is { } image)
        {
            SelectedSlot.Image = image.Crop(rect.X, rect.Y, rect.Width, rect.Height);
            ResultSlot.Result = null;
            RefreshAlignedAfterInputEdit(SelectedSlot.ChannelName!.Value);
            AppendLog($"Cropped {SelectedSlot.DisplayName} to {rect.X},{rect.Y} {rect.Width}x{rect.Height} -> {SelectedSlot.Image.Width}x{SelectedSlot.Image.Height}.");
            Status = $"Cropped {SelectedSlot.DisplayName} to {SelectedSlot.Image.Width} x {SelectedSlot.Image.Height}.";
        }

        SelectionRect = ImageSelectionRect.Empty;
        RefreshPreviewImageContext();
    }

    [RelayCommand(CanExecute = nameof(CanDetectAutoCleanMask))]
    private async Task AutoCleanSelectedChannel()
    {
        await RunOperation(async () =>
        {
            var progressScope = BeginAutoCleanProgress();
            try
            {
                if (!TryGetSelectedAutoCleanInputs(out var channelName, out var target, out var other1, out var other2))
                {
                    return;
                }

                Status = $"Detecting dust/scratch mask for {SelectedSlot!.DisplayName}...";
                var settings = new AutoCleanSettings(AutoCleanSensitivity, AutoCleanRadius);
                var progress = CreateAutoCleanProgress(progressScope);
                var result = await Task.Run(
                    () => ChannelRetoucher.DetectSingleChannelDefects(target, other1, other2, settings, progress));
                if (result.CandidatePixels == 0)
                {
                    Status = $"No dust/scratch candidates found in {SelectedSlot.DisplayName}.";
                    AppendLog($"Auto-clean mask {SelectedSlot.DisplayName}: no candidates.");
                    return;
                }

                BeginAutoCleanMaskReview(channelName, result.Mask);
                Status = $"Review auto-clean mask for {SelectedSlot.DisplayName}: {result.CandidatePixels} candidate pixels.";
                AppendLog($"Auto-clean mask {SelectedSlot.DisplayName}: {result.CandidatePixels} candidate pixels, sensitivity {settings.NormalizedSensitivity}.");
            }
            finally
            {
                EndAutoCleanProgress(progressScope);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanApplyAutoCleanMask))]
    private async Task ApplyAutoCleanMask()
    {
        if (SelectedSlot is not { Image: { } image, ChannelName: { } channelName } ||
            PendingAutoCleanMask is not { } mask ||
            PendingAutoCleanChannel != channelName)
        {
            return;
        }

        var changedPixels = mask.Count(value => value > 0);
        if (changedPixels == 0)
        {
            ClearPendingAutoCleanMask();
            Status = $"Auto-clean mask for {SelectedSlot.DisplayName} is empty.";
            return;
        }

        PushUndo();
        IsBusy = true;
        var progressScope = BeginAutoCleanProgress();
        try
        {
            var options = CreateHealOptions();
            TryGetHealingGuides(channelName, out var guide1, out var guide2);
            var progress = CreateAutoCleanProgress(progressScope);
            var result = await Task.Run(() => ChannelHealer.HealChannel(image, guide1, guide2, mask, options, progress));
            SelectedSlot.Image = result.Image;
            ClearPendingAutoCleanMask();
            RefreshAlignedAfterInputEdit(channelName);
            RefreshPreviewImageContext();
            Status = result.StatusMessage ??
                     $"Applied auto-clean mask to {SelectedSlot.DisplayName}: {changedPixels} masked pixels.";
            AppendLog($"Auto-clean apply {SelectedSlot.DisplayName}: {changedPixels} masked pixels, mode {options.Mode}.");
        }
        finally
        {
            EndAutoCleanProgress(progressScope);
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelAutoCleanMask))]
    private void CancelAutoCleanMask()
    {
        var channel = SelectedSlot?.DisplayName ?? PendingAutoCleanChannel?.ToString() ?? "channel";
        ClearPendingAutoCleanMask();
        Status = $"Canceled auto-clean mask for {channel}.";
        AppendLog($"Auto-clean mask canceled for {channel}.");
    }

    [RelayCommand(CanExecute = nameof(CanEditAutoCleanMask))]
    private void EditAutoCleanMask(AutoCleanMaskEditOperation? operation)
    {
        if (operation is null ||
            PendingAutoCleanMask is not { } mask ||
            SelectedSlot?.Image is not { } image)
        {
            return;
        }

        pendingAutoCleanBaseMask ??= (byte[])mask.Clone();
        EnsureAutoCleanEditLayers(mask.Length);

        var editMask = operation.IsRectangle
            ? CreateRectangleMaskEdit(image.Width, image.Height, operation)
            : CreateBrushMaskEdit(image.Width, image.Height, operation);
        ApplyManualAutoCleanMaskEdit(editMask, operation.Action);
        RebuildPendingAutoCleanMaskFromLayers();
    }

    [RelayCommand(CanExecute = nameof(CanApplyRetouchStroke))]
    private async Task ApplyRetouchStroke(RetouchStroke? stroke)
    {
        if (stroke is not { Points.Count: > 0 } || SelectedSlot is not { Image: { } image, ChannelName: { } channelName })
        {
            return;
        }

        var mask = ChannelRetoucher.CreateBrushMask(image.Width, image.Height, [stroke]);
        if (!mask.Any(value => value > 0))
        {
            return;
        }

        PushUndo();
        IsBusy = true;
        try
        {
            var options = CreateHealOptions();
            TryGetHealingGuides(channelName, out var guide1, out var guide2);
            var result = await Task.Run(() => ChannelHealer.HealChannel(image, guide1, guide2, mask, options));
            SelectedSlot.Image = result.Image;
            RefreshAlignedAfterInputEdit(channelName);
            RefreshPreviewImageContext();
            var count = mask.Count(value => value > 0);
            Status = result.StatusMessage ?? $"Retouched {SelectedSlot.DisplayName}: {count} masked pixels.";
            AppendLog($"Brush retouch {SelectedSlot.DisplayName}: {count} masked pixels, mode {options.Mode}.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyStampStroke))]
    private void ApplyStampStroke(CloneStampStroke? stroke)
    {
        if (stroke is not { DestinationStroke.Points.Count: > 0 } ||
            SelectedSlot is not { Image: { } image, ChannelName: { } channelName })
        {
            return;
        }

        var result = ChannelRetoucher.Stamp(image, stroke);
        if (!result.Mask.Any(value => value > 0))
        {
            return;
        }

        PushUndo();
        SelectedSlot.Image = result.Image;
        RefreshAlignedAfterInputEdit(channelName);
        RefreshPreviewImageContext();
        var count = result.Mask.Count(value => value > 0);
        Status = $"Stamped {SelectedSlot.DisplayName}: {count} blended pixels.";
        AppendLog($"Clone stamp {SelectedSlot.DisplayName}: {count} blended pixels, brush {stroke.DestinationStroke.BrushSize}, blend {Math.Clamp(stroke.BlendWidth, 1, 24)}.");
    }

    [RelayCommand]
    private void ResetExposure()
    {
        PushUndo();
        suppressUndoCapture = true;
        try
        {
            RedExposureStops = 0.0;
            GreenExposureStops = 0.0;
            BlueExposureStops = 0.0;
        }
        finally
        {
            suppressUndoCapture = false;
        }

        ScheduleResultRebuild();
        AppendLog("Exposure reset.");
    }

    [RelayCommand(CanExecute = nameof(CanPickWhiteBalance))]
    private void PickWhiteBalance(RetouchPoint point)
    {
        if (ResultSlot.Result is not { } result)
        {
            return;
        }

        var x = Math.Clamp((int)MathF.Round(point.X), 0, result.Width - 1);
        var y = Math.Clamp((int)MathF.Round(point.Y), 0, result.Height - 1);

        BeginExposureOrColorEdit();
        whiteBalancePipetteX = x;
        whiteBalancePipetteY = y;
        if (AutoWhiteBalance)
        {
            AutoWhiteBalance = false;
        }
        else
        {
            ScheduleResultRebuild();
        }

        Status = $"White balance picked at {x}, {y}.";
        AppendLog($"White balance pipette: {x},{y}, radius {WhiteBalancePipetteRadius}.");
    }

    [RelayCommand]
    private void ClearLog()
    {
        processingLog.Clear();
        OnPropertyChanged(nameof(ProcessingLogText));
    }

    [RelayCommand]
    private void ToggleExportSettings()
    {
        IsExportSettingsOpen = !IsExportSettingsOpen;
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task OpenRed()
    {
        return OpenChannel(ChannelName.Red);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task OpenGreen()
    {
        return OpenChannel(ChannelName.Green);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task OpenBlue()
    {
        return OpenChannel(ChannelName.Blue);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task OpenTriptych()
    {
        await RunOperation(async () =>
        {
            var path = await fileDialogService.OpenImage();
            if (path is null)
            {
                return;
            }

            Status = $"Loading triptych {Path.GetFileName(path)}...";
            AppendLog($"Loading triptych: {path}");
            var order = SelectedTriptychOrder.Equals("RGB", StringComparison.OrdinalIgnoreCase)
                ? TriptychOrder.Rgb
                : TriptychOrder.Bgr;
            var channels = await Task.Run(async () =>
            {
                var image = await ImageLoader.LoadGrayscaleAsync(path);
                return TriptychSplitter.SplitTriptych(image, order);
            });

            PushUndo();
            SetChannel(RedSlot, channels[ChannelName.Red], path);
            SetChannel(GreenSlot, channels[ChannelName.Green], path);
            SetChannel(BlueSlot, channels[ChannelName.Blue], path);
            lastAligned = null;
            ResultSlot.Result = null;
            SelectedSlot = RedSlot;
            Status = $"Loaded triptych as {SelectedTriptychOrder}.";
            AppendLog($"Triptych split ({SelectedTriptychOrder}): R {RedSlot.Image!.Width}x{RedSlot.Image.Height}, G {GreenSlot.Image!.Width}x{GreenSlot.Image.Height}, B {BlueSlot.Image!.Width}x{BlueSlot.Image.Height}.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanAutoAlign))]
    private async Task AutoAlign()
    {
        await RunOperation(async () =>
        {
            ClearPendingAutoCleanMask();
            Status = "Running auto-align...";
            AppendLog("Auto-align started.");
            var channels = new Dictionary<ChannelName, ImageBuffer>
            {
                [ChannelName.Red] = RedSlot.Image!,
                [ChannelName.Green] = GreenSlot.Image!,
                [ChannelName.Blue] = BlueSlot.Image!,
            };

            AppendLog($"Input channels: R {channels[ChannelName.Red].Width}x{channels[ChannelName.Red].Height}, G {channels[ChannelName.Green].Width}x{channels[ChannelName.Green].Height}, B {channels[ChannelName.Blue].Width}x{channels[ChannelName.Blue].Height}.");

            var result = await Task.Run(() =>
            {
                var settings = CurrentPipelineSettings();
                var aligned = ReconstructionPipeline.RunAutoAlign(channels, settings.Align);
                var prepared = AlignedChannelCropper.CropToLargestFullOverlap(aligned);
                var built = ReconstructionPipeline.BuildRgb(prepared.Channels, settings);
                return (built.Rgb, built.CropInfo, Aligned: prepared.Channels);
            });

            PushUndo();
            SetPreparedChannels(result.Aligned);
            lastAligned = result.Aligned;
            ResultSlot.Result = result.Rgb;
            SelectedSlot = ResultSlot;
            Status = $"Auto-align complete. Result is {result.Rgb.Width} x {result.Rgb.Height}. {AlignChannelMetadata.FormatStatus(result.Aligned.AlignMetadata)}";
            AppendLog(AlignChannelMetadata.FormatStatus(result.Aligned.AlignMetadata));
            AppendLog(FormatCropInfo(result.CropInfo));
            AppendLog($"Result: {result.Rgb.Width}x{result.Rgb.Height}.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        await RunOperation(async () =>
        {
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
        });
    }

    public void SwapSlots(ChannelSlotViewModel source, ChannelSlotViewModel target)
    {
        if (source == target || !source.CanSwap || !target.CanSwap)
        {
            return;
        }

        ClearPendingAutoCleanMask();
        PushUndo();
        (source.Image, target.Image) = (target.Image, source.Image);
        (source.SourcePath, target.SourcePath) = (target.SourcePath, source.SourcePath);
        lastAligned = null;
        ResultSlot.Result = null;
        SelectedSlot = target;
        Status = $"Swapped {source.DisplayName} and {target.DisplayName}. Run Auto-align again.";
        AppendLog($"Swapped {source.DisplayName} and {target.DisplayName}.");
        RefreshPreviewImageContext();
    }

    private async Task OpenChannel(ChannelName channelName)
    {
        await RunOperation(async () =>
        {
            var path = await fileDialogService.OpenImage();
            if (path is null)
            {
                return;
            }

            Status = $"Loading {channelName} channel...";
            AppendLog($"Loading {channelName} channel: {path}");
            var image = await Task.Run(async () =>
            {
                var loaded = await ImageLoader.LoadGrayscaleAsync(path);
                return ImageLoader.TrimBlackBorders(loaded);
            });
            var slot = GetSlot(channelName);
            PushUndo();
            SetChannel(slot, image, path);
            lastAligned = null;
            ResultSlot.Result = null;
            SelectedSlot = slot;
            Status = $"Loaded {channelName} from {Path.GetFileName(path)}.";
            AppendLog($"{channelName} loaded: {image.Width}x{image.Height} (trimmed).");
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
    }

    private void SetPreparedChannels(AlignedChannels aligned)
    {
        ClearPendingAutoCleanMask();
        RedSlot.Image = aligned.Red;
        GreenSlot.Image = aligned.Green;
        BlueSlot.Image = aligned.Blue;
        RefreshPreviewImageContext();
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
            lastAligned = null;
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
            lastAligned = AlignedChannelCropper.Crop(aligned, cropInfo);
        }
        else
        {
            lastAligned = null;
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
            PatchRadius: AutoCleanRadius,
            DebugOutput: DebugHealOutput);
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
        if (PendingAutoCleanMask is not { } mask ||
            SelectedSlot?.Image is not { } image ||
            mask.Length != image.Width * image.Height)
        {
            AutoCleanMaskOverlayBitmap = null;
            return;
        }

        AutoCleanMaskOverlayBitmap = AvaloniaBitmapFactory.FromMaskOverlay(mask, image.Width, image.Height);
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
            var settings = new AutoCleanSettings(AutoCleanSensitivity, AutoCleanRadius);
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
            Align = new AlignOptions(TrimBorders: false),
            Color = new ColorSettings(
                AutoWhiteBalance: AutoWhiteBalance,
                PipetteActive: HasPipetteWhiteBalance && !AutoWhiteBalance,
                PipetteX: whiteBalancePipetteX,
                PipetteY: whiteBalancePipetteY,
                PipetteRadius: WhiteBalancePipetteRadius),
            Exposure = new ChannelExposureSettings(
                (float)RedExposureStops,
                (float)GreenExposureStops,
                (float)BlueExposureStops),
            Crop = new CropSettings { SkipCrop = skipCrop },
        };
    }

    private bool HasPipetteWhiteBalance => whiteBalancePipetteX >= 0 && whiteBalancePipetteY >= 0;

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
            lastAligned = ReplaceAlignedChannel(aligned, channelName, image);
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
        lastAligned = channelName switch
        {
            ChannelName.Red => aligned with { Red = transformed.Image, MaskRed = transformed.Mask },
            ChannelName.Green => aligned with { Green = transformed.Image, MaskGreen = transformed.Mask },
            ChannelName.Blue => aligned with { Blue = transformed.Image, MaskBlue = transformed.Mask },
            _ => aligned,
        };

        ScheduleResultRebuild();
    }

    private void ClearAlignedAfterInputEdit()
    {
        lastAligned = null;
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
            var aligned = lastAligned;
            if (aligned is null)
            {
                return;
            }

            var settings = CurrentPipelineSettings(skipCrop: true);
            var result = await Task.Run(() => ReconstructionPipeline.BuildRgb(aligned, settings), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ResultSlot.Result = result.Rgb;
            RefreshPreviewBindings();
            Status = $"Result rebuilt: {result.Rgb.Width} x {result.Rgb.Height}.";
            AppendLog("Result rebuilt from cached alignment.");
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

    private bool CanStartOperation()
    {
        return !IsBusy;
    }

    private bool CanAutoAlign()
    {
        return !IsBusy && RedSlot.Image is not null && GreenSlot.Image is not null && BlueSlot.Image is not null;
    }

    private bool CanExport()
    {
        return !IsBusy && ResultSlot.Result is not null;
    }

    private bool CanExportChannels()
    {
        return !IsBusy && RedSlot.Image is not null && GreenSlot.Image is not null && BlueSlot.Image is not null;
    }

    private bool CanCropToSelection()
    {
        return !IsBusy &&
               !IsAutoCleanMaskPending &&
               SelectedSlot is { HasImage: true } &&
               !SelectionRect.IsEmpty;
    }

    private bool CanEditSelectedInputChannel()
    {
        return !IsBusy && !IsAutoCleanMaskPending && SelectedSlot is { Image: not null, ChannelName: not null };
    }

    private bool CanDetectAutoCleanMask()
    {
        return !IsBusy && !IsAutoCleanMaskPending && TryGetSelectedAutoCleanInputs(out _, out _, out _, out _);
    }

    private bool CanApplyAutoCleanMask()
    {
        return !IsBusy &&
               PendingAutoCleanMask is { Length: > 0 } &&
               PendingAutoCleanChannel.HasValue &&
               SelectedSlot is { Image: not null, ChannelName: { } channelName } &&
               channelName == PendingAutoCleanChannel;
    }

    private bool CanCancelAutoCleanMask()
    {
        return !IsBusy && IsAutoCleanMaskPending;
    }

    private bool CanEditAutoCleanMask(AutoCleanMaskEditOperation? operation)
    {
        return !IsBusy && IsAutoCleanMaskPending && operation is not null;
    }

    private bool CanApplyRetouchStroke(RetouchStroke? stroke)
    {
        return CanEditSelectedInputChannel() && stroke is { Points.Count: > 0 };
    }

    private bool CanApplyStampStroke(CloneStampStroke? stroke)
    {
        return CanEditSelectedInputChannel() && stroke is { DestinationStroke.Points.Count: > 0 };
    }

    private bool CanPickWhiteBalance(RetouchPoint point)
    {
        return CanUseWhiteBalancePicker;
    }

    private bool CanUndo()
    {
        return !IsBusy && undoHistory.Count > 0;
    }

    private bool CanRedo()
    {
        return !IsBusy && redoHistory.Count > 0;
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
    }

    partial void OnSelectionRectChanged(ImageSelectionRect value)
    {
        CropToSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnToolModeChanged(EditorToolMode value)
    {
        SelectionRect = ImageSelectionRect.Empty;
        OnPropertyChanged(nameof(IsSelectToolMode));
        OnPropertyChanged(nameof(IsHealToolMode));
        OnPropertyChanged(nameof(IsCloneToolMode));
        OnPropertyChanged(nameof(IsWhiteBalancePickerToolMode));
    }

    partial void OnAutoCleanSensitivityChanged(int value)
    {
        SchedulePendingAutoCleanMaskRefresh();
    }

    partial void OnPendingAutoCleanMaskChanged(byte[]? value)
    {
        RefreshAutoCleanMaskOverlay();
        RefreshPreviewBindings();
        NotifyAutoCleanCommands();
    }

    partial void OnPendingAutoCleanChannelChanged(ChannelName? value)
    {
        RefreshPreviewBindings();
        NotifyAutoCleanCommands();
    }

    partial void OnShowAutoCleanResultPreviewChanged(bool value)
    {
        RefreshPreviewBindings();
    }

    partial void OnExportFormatChanged(RgbExportFormat value)
    {
        SaveExportSettings();
    }

    partial void OnLimitExportSizeChanged(bool value)
    {
        SaveExportSettings();
    }

    partial void OnExportMaxSideChanged(int value)
    {
        SaveExportSettings();
    }

    partial void OnPngCompressionChanged(int value)
    {
        SaveExportSettings();
    }

    partial void OnJpegQualityChanged(int value)
    {
        SaveExportSettings();
    }

    partial void OnTiffCompressionChanged(TiffExportCompression value)
    {
        OnPropertyChanged(nameof(IsTiffDeflate));
        SaveExportSettings();
    }

    partial void OnTiffDeflateLevelChanged(int value)
    {
        SaveExportSettings();
    }

    partial void OnAutoWhiteBalanceChanging(bool oldValue, bool newValue)
    {
        BeginExposureOrColorEdit();
    }

    partial void OnAutoWhiteBalanceChanged(bool value)
    {
        ScheduleResultRebuild();
    }

    partial void OnRedExposureStopsChanging(double oldValue, double newValue)
    {
        BeginExposureOrColorEdit();
    }

    partial void OnGreenExposureStopsChanging(double oldValue, double newValue)
    {
        BeginExposureOrColorEdit();
    }

    partial void OnBlueExposureStopsChanging(double oldValue, double newValue)
    {
        BeginExposureOrColorEdit();
    }

    partial void OnRedExposureStopsChanged(double value)
    {
        ScheduleResultRebuild();
    }

    partial void OnGreenExposureStopsChanged(double value)
    {
        ScheduleResultRebuild();
    }

    partial void OnBlueExposureStopsChanged(double value)
    {
        ScheduleResultRebuild();
    }

    private void BeginExposureOrColorEdit()
    {
        if (isRestoringSnapshot || suppressUndoCapture)
        {
            return;
        }

        if (!exposureUndoOpen)
        {
            PushUndo();
            exposureUndoOpen = true;
        }

        var version = ++exposureChangeVersion;
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            if (version == exposureChangeVersion)
            {
                exposureUndoOpen = false;
            }
        });
    }

    private void CloseExposureUndoWindow()
    {
        exposureChangeVersion++;
        exposureUndoOpen = false;
    }

    private void PushUndo()
    {
        if (isRestoringSnapshot || suppressUndoCapture)
        {
            return;
        }

        PushUndoSnapshot(CaptureSnapshot());
    }

    private void PushUndoSnapshot(EditorSnapshot snapshot)
    {
        undoHistory.Add(snapshot);
        if (undoHistory.Count > UndoLimit)
        {
            undoHistory.RemoveAt(0);
        }

        redoHistory.Clear();
        NotifyHistoryCommands();
    }

    private EditorSnapshot CaptureSnapshot()
    {
        return new EditorSnapshot(
            RedSlot.Image?.Clone(),
            GreenSlot.Image?.Clone(),
            BlueSlot.Image?.Clone(),
            RedSlot.SourcePath,
            GreenSlot.SourcePath,
            BlueSlot.SourcePath,
            ResultSlot.Result?.Clone(),
            CloneAligned(lastAligned),
            RedExposureStops,
            GreenExposureStops,
            BlueExposureStops,
            AutoWhiteBalance,
            whiteBalancePipetteX,
            whiteBalancePipetteY,
            SelectedSlot?.DisplayName);
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        ClearPendingAutoCleanMask();
        isRestoringSnapshot = true;
        try
        {
            RedSlot.Image = snapshot.Red?.Clone();
            GreenSlot.Image = snapshot.Green?.Clone();
            BlueSlot.Image = snapshot.Blue?.Clone();
            RedSlot.SourcePath = snapshot.RedSourcePath;
            GreenSlot.SourcePath = snapshot.GreenSourcePath;
            BlueSlot.SourcePath = snapshot.BlueSourcePath;
            ResultSlot.Result = snapshot.Result?.Clone();
            lastAligned = CloneAligned(snapshot.LastAligned);
            RedExposureStops = snapshot.RedExposureStops;
            GreenExposureStops = snapshot.GreenExposureStops;
            BlueExposureStops = snapshot.BlueExposureStops;
            AutoWhiteBalance = snapshot.AutoWhiteBalance;
            whiteBalancePipetteX = snapshot.WhiteBalancePipetteX;
            whiteBalancePipetteY = snapshot.WhiteBalancePipetteY;
            SelectedSlot = snapshot.SelectedSlotDisplayName switch
            {
                "Red" => RedSlot,
                "Green" => GreenSlot,
                "Blue" => BlueSlot,
                "Result" => ResultSlot,
                _ => RedSlot,
            };
        }
        finally
        {
            isRestoringSnapshot = false;
        }

        RefreshPreviewImageContext();
    }

    private static AlignedChannels? CloneAligned(AlignedChannels? aligned)
    {
        if (aligned is null)
        {
            return null;
        }

        return new AlignedChannels(
            aligned.Red.Clone(),
            aligned.Green.Clone(),
            aligned.Blue.Clone(),
            (byte[])aligned.MaskRed.Clone(),
            (byte[])aligned.MaskGreen.Clone(),
            (byte[])aligned.MaskBlue.Clone(),
            aligned.AlignMetadata,
            CloneTransforms(aligned.AlignTransforms));
    }

    private static IReadOnlyDictionary<ChannelName, ChannelAlignmentTransform>? CloneTransforms(
        IReadOnlyDictionary<ChannelName, ChannelAlignmentTransform>? transforms)
    {
        if (transforms is null)
        {
            return null;
        }

        return transforms.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Matrix = (double[])pair.Value.Matrix.Clone(),
                Shifts = pair.Value.Shifts.ToArray(),
            });
    }

    private static byte[] FullMask(ImageBuffer image)
    {
        var mask = new byte[image.Width * image.Height];
        Array.Fill(mask, (byte)1);
        return mask;
    }

    private void NotifyHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(PreviewInteractionMode));
        OnPropertyChanged(nameof(CanUseWhiteBalancePicker));
        PickWhiteBalanceCommand.NotifyCanExecuteChanged();
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
        processingLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        OnPropertyChanged(nameof(ProcessingLogText));
    }

    private static string FormatCropInfo(CropInfo cropInfo)
    {
        var cropWidth = cropInfo.X1 - cropInfo.X0;
        var cropHeight = cropInfo.Y1 - cropInfo.Y0;
        return $"Auto-crop: ({cropInfo.X0},{cropInfo.Y0})-({cropInfo.X1},{cropInfo.Y1}) => {cropWidth}x{cropHeight}; overlap ({cropInfo.OverlapX0},{cropInfo.OverlapY0})-({cropInfo.OverlapX1},{cropInfo.OverlapY1}).";
    }

    private sealed record EditorSnapshot(
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
        string? SelectedSlotDisplayName);
}
