using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    private void SetLastAligned(AlignedChannels? aligned)
    {
        lastAligned = aligned;
        RebuildResultCommand.NotifyCanExecuteChanged();
        CropOverlapCommand.NotifyCanExecuteChanged();
        RefreshChannelStates();
        OnPropertyChanged(nameof(RedAlignSummary));
        OnPropertyChanged(nameof(GreenAlignSummary));
        OnPropertyChanged(nameof(BlueAlignSummary));
    }

    [RelayCommand(CanExecute = nameof(CanRebuildResult))]
    private void RebuildResult()
    {
        ScheduleResultRebuild();
        Status = "Rebuilding result from aligned channels...";
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
                var aligned = ReconstructionPipeline.RunAutoAlign(channels, settings.Align, settings.Diagnostics);
                var prepared = AlignedChannelCropper.CropToLargestFullOverlap(aligned);
                var built = ReconstructionPipeline.BuildRgb(prepared.Channels, settings);
                return (built.Rgb, built.CropInfo, Aligned: prepared.Channels);
            });

            RecordSnapshotCommand("AutoAlign");
            autoCleanSessionCache.Clear();
            SetPreparedChannels(result.Aligned);
            SetLastAligned(result.Aligned);
            ResultSlot.Result = result.Rgb;
            SelectedSlot = ResultSlot;
            Status = $"Auto-align complete. Result is {result.Rgb.Width} x {result.Rgb.Height}. {AlignChannelMetadata.FormatStatus(result.Aligned.AlignMetadata)}";
            AppendLog(AlignChannelMetadata.FormatStatus(result.Aligned.AlignMetadata));
            AppendLog(FormatCropInfo(result.CropInfo));
            AppendLog($"Result: {result.Rgb.Width}x{result.Rgb.Height}.");
        });
    }

    private bool CanRebuildResult() => !IsBusy && lastAligned is not null;

    private bool CanAutoAlign()
    {
        return !IsBusy && RedSlot.Image is not null && GreenSlot.Image is not null && BlueSlot.Image is not null;
    }

    private void RestoreLastAlignedIfPrepared()
    {
        if (ResultSlot.Result is null || !TryGetWorkingChannels(out var red, out var green, out var blue))
        {
            SetLastAligned(null);
            return;
        }

        var metadata = new Dictionary<ChannelName, AlignChannelMetadata>
        {
            [ChannelName.Red] = new("loaded", 0),
            [ChannelName.Green] = new("loaded", 0),
            [ChannelName.Blue] = new("loaded", 0),
        };

        SetLastAligned(AlignedChannelCropper.FromPreparedChannels(red, green, blue, metadata));
    }
}
