using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Crop;
using Prokudin.Core.Pipeline;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand]
    private void ResetCropSelection()
    {
        SelectionRect = ImageSelectionRect.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanCropOverlap))]
    private async Task CropOverlap()
    {
        if (lastAligned is null)
        {
            return;
        }

        await RunOperation(async () =>
        {
            RecordSnapshotCommand("CropOverlap");
            var (cropped, cropInfo) = await Task.Run(() =>
            {
                var (aligned, info) = AlignedChannelCropper.CropToLargestFullOverlap(lastAligned);
                return (aligned, info);
            });

            SetPreparedChannels(cropped);
            SetLastAligned(cropped);
            TranslateWhitePickForCrop(cropInfo.X0, cropInfo.Y0, cropInfo.X1 - cropInfo.X0, cropInfo.Y1 - cropInfo.Y0);
            var built = await Task.Run(() => ReconstructionPipeline.BuildRgbWithLevelsHistogram(cropped, CurrentPipelineSettings()));
            ResultSlot.Result = built.Rgb;
            LevelsHistogram = built.LevelsHistogram;
            SelectedSlot = ResultSlot;
            SelectionRect = ImageSelectionRect.Empty;
            Status = $"Cropped to largest overlap: {ResultSlot.Result!.Width} x {ResultSlot.Result.Height}.";
            AppendLog(FormatCropInfo(cropInfo));
        });
    }

    [RelayCommand(CanExecute = nameof(CanCropToSelection))]
    private void CropToSelection()
    {
        if (SelectedSlot is null || SelectionRect.IsEmpty)
        {
            return;
        }

        RecordSnapshotCommand("CropToSelection");
        var rect = SelectionRect;

        if (SelectedSlot.Result is { } rgb)
        {
            ResultSlot.Result = rgb.Crop(rect.X, rect.Y, rect.Width, rect.Height);
            LevelsHistogram = null;
            CropPreparedChannelsToResultSelection(rgb.Width, rgb.Height, rect);
            TranslateWhitePickForCrop(rect.X, rect.Y, rect.Width, rect.Height);
            AppendLog($"Cropped result to {rect.X},{rect.Y} {rect.Width}x{rect.Height} -> {ResultSlot.Result.Width}x{ResultSlot.Result.Height}.");
            Status = $"Cropped result to {ResultSlot.Result.Width} x {ResultSlot.Result.Height}.";
        }
        else if (SelectedSlot.Image is { } image)
        {
            SelectedSlot.Image = image.Crop(rect.X, rect.Y, rect.Width, rect.Height);
            ResultSlot.Result = null;
            LevelsHistogram = null;
            RefreshAlignedAfterInputEdit(SelectedSlot.ChannelName!.Value);
            AppendLog($"Cropped {SelectedSlot.DisplayName} to {rect.X},{rect.Y} {rect.Width}x{rect.Height} -> {SelectedSlot.Image.Width}x{SelectedSlot.Image.Height}.");
            Status = $"Cropped {SelectedSlot.DisplayName} to {SelectedSlot.Image.Width} x {SelectedSlot.Image.Height}.";
        }

        SelectionRect = ImageSelectionRect.Empty;
        RefreshPreviewImageContext();
        RefreshChannelStates();
    }

    private bool CanCropOverlap() => !IsBusy && lastAligned is not null;

    private void TranslateWhitePickForCrop(int x, int y, int width, int height)
    {
        if (!HasPipetteWhiteBalance)
        {
            return;
        }

        var relativeX = whiteBalancePipetteX - x;
        var relativeY = whiteBalancePipetteY - y;
        if (relativeX < 0 || relativeX >= width || relativeY < 0 || relativeY >= height)
        {
            whiteBalancePipetteX = -1;
            whiteBalancePipetteY = -1;
        }
        else
        {
            whiteBalancePipetteX = relativeX;
            whiteBalancePipetteY = relativeY;
        }

        OnPropertyChanged(nameof(WhitePickX));
        OnPropertyChanged(nameof(WhitePickY));
        OnPropertyChanged(nameof(ShowWhitePick));
        OnPropertyChanged(nameof(WhitePickQualityWarning));
    }

    private bool CanCropToSelection()
    {
        return !IsBusy &&
               !IsAutoCleanMaskPending &&
               SelectedSlot is { HasImage: true } &&
               !SelectionRect.IsEmpty;
    }
}
