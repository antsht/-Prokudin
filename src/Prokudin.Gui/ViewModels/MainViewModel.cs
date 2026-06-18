using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int UndoLimit = 20;
    private readonly IFileDialogService fileDialogService;
    private readonly StringBuilder processingLog = new();
    private readonly List<EditorSnapshot> undoHistory = [];
    private readonly List<EditorSnapshot> redoHistory = [];
    private AlignedChannels? lastAligned;
    private CancellationTokenSource? resultRebuildCancellation;
    private bool isRestoringSnapshot;
    private bool suppressUndoCapture;
    private bool exposureUndoOpen;
    private int exposureChangeVersion;

    public MainViewModel(IFileDialogService fileDialogService)
    {
        this.fileDialogService = fileDialogService;

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

        SelectedSlot = RedSlot;
    }

    public ChannelSlotViewModel RedSlot { get; }

    public ChannelSlotViewModel GreenSlot { get; }

    public ChannelSlotViewModel BlueSlot { get; }

    public ChannelSlotViewModel ResultSlot { get; }

    public ObservableCollection<ChannelSlotViewModel> Slots { get; }

    public IReadOnlyList<string> TriptychOrders { get; } = ["RGB", "BGR"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CropToSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoCleanSelectedChannelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRetouchStrokeCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(ApplyRetouchStrokeCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
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
    private bool isRetouchMode;

    [ObservableProperty]
    private int brushSize = 12;

    [ObservableProperty]
    private int autoCleanSensitivity = 50;

    [ObservableProperty]
    private int autoCleanRadius = 3;

    [ObservableProperty]
    private bool autoWhiteBalance = true;

    [ObservableProperty]
    private double redExposureStops;

    [ObservableProperty]
    private double greenExposureStops;

    [ObservableProperty]
    private double blueExposureStops;

    public PreviewInteractionMode PreviewInteractionMode =>
        IsRetouchMode ? PreviewInteractionMode.Retouch : PreviewInteractionMode.Selection;

    public string FitToWindowButtonText =>
        PreviewZoomMode == PreviewZoomMode.FitToWindow ? "1:1" : "Fit to window";

    public string ProcessingLogText => processingLog.ToString();

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
            lastAligned = null;
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
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedInputChannel))]
    private async Task AutoCleanSelectedChannel()
    {
        await RunOperation(async () =>
        {
            if (SelectedSlot is not { Image: { } image, ChannelName: { } channelName })
            {
                return;
            }

            Status = $"Auto-cleaning {SelectedSlot.DisplayName}...";
            var settings = new AutoCleanSettings(AutoCleanSensitivity, AutoCleanRadius);
            var result = await Task.Run(() => ChannelRetoucher.AutoClean(image, settings));
            var changedPixels = result.Mask.Count(value => value > 0);
            if (changedPixels == 0)
            {
                Status = $"No dust/scratch candidates found in {SelectedSlot.DisplayName}.";
                AppendLog($"Auto-clean {SelectedSlot.DisplayName}: no candidates.");
                return;
            }

            PushUndo();
            SelectedSlot.Image = result.Image;
            RefreshAlignedAfterInputEdit(channelName);
            Status = $"Auto-cleaned {SelectedSlot.DisplayName}: {changedPixels} masked pixels.";
            AppendLog($"Auto-clean {SelectedSlot.DisplayName}: {changedPixels} masked pixels, radius {settings.NormalizedInpaintRadius}, sensitivity {settings.NormalizedSensitivity}.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanApplyRetouchStroke))]
    private void ApplyRetouchStroke(RetouchStroke? stroke)
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
        SelectedSlot.Image = ChannelRetoucher.InpaintMask(image, mask, AutoCleanRadius);
        RefreshAlignedAfterInputEdit(channelName);
        var count = mask.Count(value => value > 0);
        Status = $"Retouched {SelectedSlot.DisplayName}: {count} masked pixels.";
        AppendLog($"Brush retouch {SelectedSlot.DisplayName}: {count} masked pixels, brush {stroke.BrushSize}, radius {AutoCleanRadius}.");
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

    [RelayCommand]
    private void ClearLog()
    {
        processingLog.Clear();
        OnPropertyChanged(nameof(ProcessingLogText));
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
                var built = ReconstructionPipeline.BuildRgb(aligned, settings);
                return (built.Rgb, built.CropInfo, Aligned: aligned);
            });

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
            var path = await fileDialogService.SavePng();
            var result = ResultSlot.Result;
            if (path is null || result is null)
            {
                return;
            }

            Status = $"Exporting {Path.GetFileName(path)}...";
            AppendLog($"Exporting {result.Width}x{result.Height} to {path}");
            await Task.Run(async () => await ImageLoader.SavePngAsync(path, result));
            Status = $"Exported {path}.";
            AppendLog($"Export complete: {path}");
        });
    }

    public void SwapSlots(ChannelSlotViewModel source, ChannelSlotViewModel target)
    {
        if (source == target || !source.CanSwap || !target.CanSwap)
        {
            return;
        }

        PushUndo();
        (source.Image, target.Image) = (target.Image, source.Image);
        (source.SourcePath, target.SourcePath) = (target.SourcePath, source.SourcePath);
        lastAligned = null;
        ResultSlot.Result = null;
        SelectedSlot = target;
        Status = $"Swapped {source.DisplayName} and {target.DisplayName}. Run Auto-align again.";
        AppendLog($"Swapped {source.DisplayName} and {target.DisplayName}.");
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
            CropToSelectionCommand.NotifyCanExecuteChanged();
            AutoCleanSelectedChannelCommand.NotifyCanExecuteChanged();
            ApplyRetouchStrokeCommand.NotifyCanExecuteChanged();
            NotifyHistoryCommands();
        }
    }

    private void SetChannel(ChannelSlotViewModel slot, ImageBuffer image, string sourcePath)
    {
        slot.Image = image;
        slot.SourcePath = sourcePath;
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

    private PipelineSettings CurrentPipelineSettings()
    {
        return new PipelineSettings
        {
            Align = new AlignOptions(TrimBorders: false),
            Color = new ColorSettings(AutoWhiteBalance: AutoWhiteBalance),
            Exposure = new ChannelExposureSettings(
                (float)RedExposureStops,
                (float)GreenExposureStops,
                (float)BlueExposureStops),
        };
    }

    private void RefreshAlignedAfterInputEdit(ChannelName channelName)
    {
        if (lastAligned is not { } aligned ||
            aligned.AlignTransforms?.TryGetValue(channelName, out var transform) != true)
        {
            ClearAlignedAfterInputEdit();
            return;
        }

        var slot = GetSlot(channelName);
        if (slot.Image is not { } image || !transform.CanApplyTo(image))
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

            var settings = CurrentPipelineSettings();
            var result = await Task.Run(() => ReconstructionPipeline.BuildRgb(aligned, settings), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ResultSlot.Result = result.Rgb;
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

    private bool CanCropToSelection()
    {
        return !IsBusy &&
               SelectedSlot is { HasImage: true } &&
               !SelectionRect.IsEmpty;
    }

    private bool CanEditSelectedInputChannel()
    {
        return !IsBusy && SelectedSlot is { Image: not null, ChannelName: not null };
    }

    private bool CanApplyRetouchStroke(RetouchStroke? stroke)
    {
        return CanEditSelectedInputChannel() && stroke is { Points.Count: > 0 };
    }

    private bool CanUndo()
    {
        return !IsBusy && undoHistory.Count > 0;
    }

    private bool CanRedo()
    {
        return !IsBusy && redoHistory.Count > 0;
    }

    partial void OnSelectedSlotChanged(ChannelSlotViewModel? value)
    {
        SelectionRect = ImageSelectionRect.Empty;
        AutoCleanSelectedChannelCommand.NotifyCanExecuteChanged();
        ApplyRetouchStrokeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectionRectChanged(ImageSelectionRect value)
    {
        CropToSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRetouchModeChanged(bool value)
    {
        SelectionRect = ImageSelectionRect.Empty;
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
            SelectedSlot?.DisplayName);
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
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

    private void NotifyHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
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
        string? SelectedSlotDisplayName);
}
