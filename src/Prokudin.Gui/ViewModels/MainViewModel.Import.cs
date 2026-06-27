using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
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

            RecordSnapshotCommand("ImportTriptych");
            SetChannel(RedSlot, channels[ChannelName.Red], path);
            SetChannel(GreenSlot, channels[ChannelName.Green], path);
            SetChannel(BlueSlot, channels[ChannelName.Blue], path);
            SetLastAligned(null);
            ResultSlot.Result = null;
            SelectedSlot = RedSlot;
            Status = $"Loaded triptych as {SelectedTriptychOrder}.";
            AppendLog($"Triptych split ({SelectedTriptychOrder}): R {RedSlot.Image!.Width}x{RedSlot.Image.Height}, G {GreenSlot.Image!.Width}x{GreenSlot.Image.Height}, B {BlueSlot.Image!.Width}x{BlueSlot.Image.Height}.");
        });
    }

    public void SwapSlots(ChannelSlotViewModel source, ChannelSlotViewModel target)
    {
        if (source == target || !source.CanSwap || !target.CanSwap)
        {
            return;
        }

        ClearPendingAutoCleanMask();
        RecordSnapshotCommand("SwapChannels");
        (source.Image, target.Image) = (target.Image, source.Image);
        (source.SourcePath, target.SourcePath) = (target.SourcePath, source.SourcePath);
        SetLastAligned(null);
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
            RecordSnapshotCommand("ImportChannel");
            SetChannel(slot, image, path);
            SetLastAligned(null);
            ResultSlot.Result = null;
            SelectedSlot = slot;
            Status = $"Loaded {channelName} from {Path.GetFileName(path)}.";
            AppendLog($"{channelName} loaded: {image.Width}x{image.Height} (trimmed).");
        });
    }

    private bool CanStartOperation()
    {
        return !IsBusy;
    }
}
