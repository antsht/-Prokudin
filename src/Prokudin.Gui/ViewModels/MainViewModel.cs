using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Crop;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFileDialogService fileDialogService;
    private readonly StringBuilder processingLog = new();

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
    private ChannelSlotViewModel? selectedSlot;

    [ObservableProperty]
    private string selectedTriptychOrder = "BGR";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenGreenCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBlueCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTriptychCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoAlignCommand))]
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

    [RelayCommand(CanExecute = nameof(CanCropToSelection))]
    private void CropToSelection()
    {
        if (SelectedSlot is null || SelectionRect.IsEmpty)
        {
            return;
        }

        var rect = SelectionRect;

        if (SelectedSlot.Result is { } rgb)
        {
            ResultSlot.Result = rgb.Crop(rect.X, rect.Y, rect.Width, rect.Height);
            AppendLog($"Cropped result to {rect.X},{rect.Y} {rect.Width}x{rect.Height} -> {ResultSlot.Result.Width}x{ResultSlot.Result.Height}.");
            Status = $"Cropped result to {ResultSlot.Result.Width} x {ResultSlot.Result.Height}.";
        }
        else if (SelectedSlot.Image is { } image)
        {
            SelectedSlot.Image = image.Crop(rect.X, rect.Y, rect.Width, rect.Height);
            ResultSlot.Result = null;
            AppendLog($"Cropped {SelectedSlot.DisplayName} to {rect.X},{rect.Y} {rect.Width}x{rect.Height} -> {SelectedSlot.Image.Width}x{SelectedSlot.Image.Height}.");
            Status = $"Cropped {SelectedSlot.DisplayName} to {SelectedSlot.Image.Width} x {SelectedSlot.Image.Height}.";
        }

        SelectionRect = ImageSelectionRect.Empty;
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
            SetChannel(RedSlot, channels[ChannelName.Red], path);
            SetChannel(GreenSlot, channels[ChannelName.Green], path);
            SetChannel(BlueSlot, channels[ChannelName.Blue], path);
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
                var settings = new PipelineSettings
                {
                    Align = new AlignOptions(TrimBorders: false),
                };
                var aligned = ReconstructionPipeline.RunAutoAlign(channels, settings.Align);
                var built = ReconstructionPipeline.BuildRgb(aligned, settings);
                return (built.Rgb, built.CropInfo, aligned.AlignMetadata);
            });

            ResultSlot.Result = result.Rgb;
            SelectedSlot = ResultSlot;
            Status = $"Auto-align complete. Result is {result.Rgb.Width} x {result.Rgb.Height}. {AlignChannelMetadata.FormatStatus(result.AlignMetadata)}";
            AppendLog(AlignChannelMetadata.FormatStatus(result.AlignMetadata));
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

        (source.Image, target.Image) = (target.Image, source.Image);
        (source.SourcePath, target.SourcePath) = (target.SourcePath, source.SourcePath);
        ResultSlot.Result = null;
        SelectedSlot = target;
        Status = $"Swapped {source.DisplayName} and {target.DisplayName}.";
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
            SetChannel(slot, image, path);
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

    partial void OnSelectedSlotChanged(ChannelSlotViewModel? value)
    {
        SelectionRect = ImageSelectionRect.Empty;
    }

    partial void OnSelectionRectChanged(ImageSelectionRect value)
    {
        CropToSelectionCommand.NotifyCanExecuteChanged();
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
}
