using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
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
                var (settings, _) = CreateAutoCleanResolvedSettings(channelName);
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

        RecordSnapshotCommand("AutoCleanApply");
        IsBusy = true;
        var progressScope = BeginAutoCleanProgress();
        try
        {
            var (_, options) = CreateAutoCleanResolvedSettings(channelName);
            TryGetHealingGuides(channelName, out var guide1, out var guide2);
            var progress = CreateAutoCleanProgress(progressScope);
            var result = await Task.Run(() => ChannelHealer.HealChannel(image, guide1, guide2, mask, options, progress))
                .ConfigureAwait(true);
            await ApplyRetouchResultAsync(channelName, result.Image);
            ClearPendingAutoCleanMask();
            var healStatus = result.StatusMessage ??
                             $"Applied auto-clean mask to {SelectedSlot.DisplayName}: {changedPixels} masked pixels.";
            Status = healStatus;
            AppendLog($"Auto-clean apply {SelectedSlot.DisplayName}: {healStatus}");
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

        RecordSnapshotCommand("RetouchBrush");
        IsBusy = true;
        try
        {
            var options = CreateHealOptions();
            TryGetHealingGuides(channelName, out var guide1, out var guide2);
            var result = await Task.Run(() => ChannelHealer.HealChannel(image, guide1, guide2, mask, options))
                .ConfigureAwait(true);
            await ApplyRetouchResultAsync(channelName, result.Image);
            var count = mask.Count(value => value > 0);
            var healStatus = result.StatusMessage ?? $"Retouched {SelectedSlot!.DisplayName}: {count} masked pixels.";
            Status = healStatus;
            AppendLog($"Brush retouch {SelectedSlot.DisplayName}: {healStatus}");
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

        RecordSnapshotCommand("CloneStamp");
        ApplyRetouchResult(channelName, result.Image);
        var count = result.Mask.Count(value => value > 0);
        Status = $"Stamped {SelectedSlot.DisplayName}: {count} blended pixels.";
        AppendLog($"Clone stamp {SelectedSlot.DisplayName}: {count} blended pixels, brush {stroke.DestinationStroke.BrushSize}, blend {Math.Clamp(stroke.BlendWidth, 1, 24)}.");
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

    private async Task ApplyRetouchResultAsync(ChannelName channelName, ImageBuffer image)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyRetouchResult(channelName, image);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyRetouchResult(channelName, image));
    }

    private void ApplyRetouchResult(ChannelName channelName, ImageBuffer image)
    {
        if (GetSlot(channelName) is not { } slot)
        {
            return;
        }

        slot.Image = image;
        RefreshPreviewImageContext();
        RefreshAlignedAfterInputEdit(channelName);
    }
}
