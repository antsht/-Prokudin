using Avalonia;
using Prokudin.Core.Alignment;
using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Core.Pipeline;
using Prokudin.Core.Retouch;
using Prokudin.Gui;
using Prokudin.Gui.Services;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Tests;

public sealed class MainViewModelTests
{
    private static readonly object AvaloniaLock = new();
    private static bool avaloniaInitialized;

    [Fact]
    public void ExposureEdit_IsUndoableAndRedoable()
    {
        var viewModel = CreateViewModel();

        viewModel.RedExposureStops = 1.0;

        viewModel.UndoCommand.CanExecute(null).Should().BeTrue();
        viewModel.UndoCommand.Execute(null);
        viewModel.RedExposureStops.Should().Be(0.0);

        viewModel.RedoCommand.CanExecute(null).Should().BeTrue();
        viewModel.RedoCommand.Execute(null);
        viewModel.RedExposureStops.Should().Be(1.0);
    }

    [Fact]
    public async Task ExposureChange_RebuildsResultFromCachedAlignment()
    {
        var viewModel = CreateViewModel();
        SetInputChannels(viewModel, red: 0.25f, green: 0.25f, blue: 0.25f);
        viewModel.AutoWhiteBalance = false;
        await viewModel.AutoAlignCommand.ExecuteAsync(null);
        var width = viewModel.ResultSlot.Result!.Width;
        var height = viewModel.ResultSlot.Result.Height;

        viewModel.RedExposureStops = 1.0;
        await WaitUntil(() => viewModel.ResultSlot.Result?[0, 0, 0] > 0.45f);

        viewModel.ResultSlot.Result!.Width.Should().Be(width);
        viewModel.ResultSlot.Result.Height.Should().Be(height);
        viewModel.ResultSlot.Result![0, 0, 0].Should().BeApproximately(0.5f, 0.01f);
        viewModel.ResultSlot.Result[0, 0, 1].Should().BeApproximately(0.25f, 0.01f);
    }

    [Fact]
    public async Task AutoAlign_ReplacesInputSlotsWithPreparedChannelsMatchingResult()
    {
        var viewModel = CreateViewModel();
        SetInputChannels(viewModel, red: 0.25f, green: 0.5f, blue: 0.75f);
        viewModel.AutoWhiteBalance = false;

        await viewModel.AutoAlignCommand.ExecuteAsync(null);

        viewModel.RedSlot.Image!.Width.Should().Be(viewModel.ResultSlot.Result!.Width);
        viewModel.RedSlot.Image.Height.Should().Be(viewModel.ResultSlot.Result.Height);
        viewModel.GreenSlot.Image!.Width.Should().Be(viewModel.ResultSlot.Result.Width);
        viewModel.BlueSlot.Image!.Width.Should().Be(viewModel.ResultSlot.Result.Width);
    }

    [Fact]
    public async Task AutoWhiteBalanceToggle_RebuildsCachedResult()
    {
        var viewModel = CreateViewModel();
        SetInputChannels(viewModel, red: 0.2f, green: 0.4f, blue: 0.8f);
        viewModel.AutoWhiteBalance = false;
        await viewModel.AutoAlignCommand.ExecuteAsync(null);

        viewModel.ResultSlot.Result![0, 0, 0].Should().BeApproximately(0.2f, 0.01f);

        viewModel.AutoWhiteBalance = true;
        await WaitUntil(() => viewModel.ResultSlot.Result?[0, 0, 0] > 0.3f);

        viewModel.ResultSlot.Result![0, 0, 0].Should().BeApproximately(
            viewModel.ResultSlot.Result[0, 0, 1],
            0.05f);
    }

    [Fact]
    public async Task ApplyAutoCleanAfterAlign_ReusesCachedTransformAndKeepsResult()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(21, 21, 0.5f);
        red[10, 10] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(21, 21, 0.5f),
            ImageBuffer.Filled(21, 21, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.ApplyAutoCleanMaskCommand.Execute(null);
        await WaitUntil(() => viewModel.ResultSlot.Result is not null);

        viewModel.RedSlot.Image![10, 10].Should().BeLessThan(0.85f);
        viewModel.ResultSlot.Result.Should().NotBeNull();
    }

    [Fact]
    public void RetouchRefreshesPreviewBitmapBeforeResettingPreviewContext()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(21, 21, 0.5f);
        red[10, 10] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(21, 21, 0.5f),
            ImageBuffer.Filled(21, 21, 0.5f));
        viewModel.SelectedSlot = viewModel.RedSlot;
        var previewEvents = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.PreviewDisplayBitmap) or nameof(MainViewModel.PreviewImageContextKey))
            {
                previewEvents.Add(args.PropertyName);
            }
        };

        viewModel.ApplyRetouchStrokeCommand.Execute(new RetouchStroke(
            [new RetouchPoint(10, 10)],
            BrushSize: 3));

        var bitmapIndex = previewEvents.FindIndex(name => name == nameof(MainViewModel.PreviewDisplayBitmap));
        var contextIndex = previewEvents.FindIndex(name => name == nameof(MainViewModel.PreviewImageContextKey));
        bitmapIndex.Should().BeGreaterThanOrEqualTo(0);
        contextIndex.Should().BeGreaterThanOrEqualTo(0);
        bitmapIndex.Should().BeLessThan(contextIndex);
    }

    [Fact]
    public async Task AutoCleanAfterAlign_CreatesPendingMaskWithoutMutatingChannel()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(31, 31, 0.5f),
            ImageBuffer.Filled(31, 31, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);

        viewModel.IsAutoCleanMaskPending.Should().BeTrue();
        viewModel.PendingAutoCleanCandidatePixels.Should().BeGreaterThan(0);
        viewModel.PreviewInteractionMode.Should().Be(PreviewInteractionMode.MaskReview);
        viewModel.RedSlot.Image![15, 15].Should().BeApproximately(1.0f, 0.001f);
        viewModel.ApplyAutoCleanMaskCommand.CanExecute(null).Should().BeTrue();
        viewModel.CancelAutoCleanMaskCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAutoCleanMask_AppliesPendingMaskAndIsUndoable()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(31, 31, 0.5f),
            ImageBuffer.Filled(31, 31, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.ApplyAutoCleanMaskCommand.Execute(null);
        await WaitUntil(() => viewModel.ResultSlot.Result?[15, 15, 0] < 0.85f);

        viewModel.IsAutoCleanMaskPending.Should().BeFalse();
        viewModel.RedSlot.Image![15, 15].Should().BeLessThan(0.85f);
        viewModel.UndoCommand.Execute(null);
        viewModel.RedSlot.Image![15, 15].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public async Task CancelAutoCleanMask_ClearsPendingMaskWithoutMutationOrUndo()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(31, 31, 0.5f),
            ImageBuffer.Filled(31, 31, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        var undoWasAvailable = viewModel.UndoCommand.CanExecute(null);
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.CancelAutoCleanMaskCommand.Execute(null);

        viewModel.IsAutoCleanMaskPending.Should().BeFalse();
        viewModel.RedSlot.Image![15, 15].Should().BeApproximately(1.0f, 0.001f);
        viewModel.UndoCommand.CanExecute(null).Should().Be(undoWasAvailable);
    }

    [Fact]
    public void AutoCleanSelectedChannel_DisabledBeforeAutoAlign()
    {
        var viewModel = CreateViewModel();
        SetInputChannels(viewModel, red: 0.5f, green: 0.5f, blue: 0.5f);
        viewModel.SelectedSlot = viewModel.RedSlot;

        viewModel.AutoCleanSelectedChannelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SelectedChannelChange_ClearsPendingAutoCleanMask()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(31, 31, 0.5f),
            ImageBuffer.Filled(31, 31, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.SelectedSlot = viewModel.GreenSlot;

        viewModel.IsAutoCleanMaskPending.Should().BeFalse();
    }

    [Fact]
    public async Task EditAutoCleanMask_AddsAndRemovesBrushAndRectangleAreas()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(31, 31, 0.5f),
            ImageBuffer.Filled(31, 31, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.EditAutoCleanMaskCommand.Execute(new AutoCleanMaskEditOperation(
            new RetouchPoint(-10, -10),
            new RetouchPoint(-10, -10),
            AutoCleanMaskEditAction.Add,
            BrushSize: 3,
            IsRectangle: false));
        viewModel.PendingAutoCleanMask![0].Should().Be(1);

        viewModel.EditAutoCleanMaskCommand.Execute(new AutoCleanMaskEditOperation(
            new RetouchPoint(4, 4),
            new RetouchPoint(8, 8),
            AutoCleanMaskEditAction.Add,
            BrushSize: 3,
            IsRectangle: true));
        viewModel.PendingAutoCleanMask[(6 * 31) + 6].Should().Be(1);

        viewModel.EditAutoCleanMaskCommand.Execute(new AutoCleanMaskEditOperation(
            new RetouchPoint(6, 6),
            new RetouchPoint(6, 6),
            AutoCleanMaskEditAction.Remove,
            BrushSize: 3,
            IsRectangle: false));
        viewModel.PendingAutoCleanMask[(6 * 31) + 6].Should().Be(0);
    }

    [Fact]
    public async Task AutoCleanSensitivityChange_RebuildsPendingMaskAndKeepsManualAdds()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(41, 41, 0.5f);
        red[20, 20] = 1.0f;
        red[12, 12] = 0.57f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(41, 41, 0.5f),
            ImageBuffer.Filled(41, 41, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        viewModel.AutoCleanSensitivity = 20;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.PendingAutoCleanMask![(12 * 41) + 12].Should().Be(0);

        viewModel.EditAutoCleanMaskCommand.Execute(new AutoCleanMaskEditOperation(
            new RetouchPoint(0, 0),
            new RetouchPoint(0, 0),
            AutoCleanMaskEditAction.Add,
            BrushSize: 1,
            IsRectangle: false));
        viewModel.PendingAutoCleanMask[0].Should().Be(1);

        viewModel.AutoCleanSensitivity = 100;

        await WaitUntil(() =>
            viewModel.PendingAutoCleanMask![(12 * 41) + 12] == 1 &&
            viewModel.PendingAutoCleanMask[0] == 1);
    }

    [Fact]
    public async Task AutoCleanSensitivityChange_RebuildsPendingMaskAndKeepsManualRemoves()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(41, 41, 0.5f);
        red[20, 20] = 1.0f;
        red[12, 12] = 0.57f;
        red[28, 28] = 0.43f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(41, 41, 0.5f),
            ImageBuffer.Filled(41, 41, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        viewModel.AutoCleanSensitivity = 20;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.PendingAutoCleanMask![(12 * 41) + 12].Should().Be(0);
        viewModel.PendingAutoCleanMask[(28 * 41) + 28].Should().Be(0);

        viewModel.EditAutoCleanMaskCommand.Execute(new AutoCleanMaskEditOperation(
            new RetouchPoint(28, 28),
            new RetouchPoint(28, 28),
            AutoCleanMaskEditAction.Remove,
            BrushSize: 1,
            IsRectangle: false));

        viewModel.AutoCleanSensitivity = 100;

        await WaitUntil(() =>
            viewModel.PendingAutoCleanMask![(12 * 41) + 12] == 1 &&
            viewModel.PendingAutoCleanMask[(28 * 41) + 28] == 0);
    }

    [Fact]
    public async Task AutoCleanPreviewCanToggleToResultBitmap()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(31, 31, 0.5f);
        red[15, 15] = 1.0f;
        SetAlignedChannels(
            viewModel,
            red,
            ImageBuffer.Filled(31, 31, 0.5f),
            ImageBuffer.Filled(31, 31, 0.5f));

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        viewModel.ShowAutoCleanResultPreview = true;

        viewModel.PreviewDisplayBitmap.Should().BeSameAs(viewModel.ResultSlot.DisplayBitmap);
        viewModel.AutoCleanMaskOverlayBitmap.Should().NotBeNull();
    }

    [Fact]
    public async Task BrushRetouchAfterAlign_RebuildsPreparedResultWithoutChangingSize()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(9, 9, 0.5f);
        red[4, 4] = 1.0f;
        viewModel.RedSlot.Image = red;
        viewModel.GreenSlot.Image = ImageBuffer.Filled(9, 9, 0.5f);
        viewModel.BlueSlot.Image = ImageBuffer.Filled(9, 9, 0.5f);
        viewModel.AutoWhiteBalance = false;
        await viewModel.AutoAlignCommand.ExecuteAsync(null);
        var width = viewModel.ResultSlot.Result!.Width;
        var height = viewModel.ResultSlot.Result.Height;

        viewModel.SelectedSlot = viewModel.RedSlot;
        viewModel.ApplyRetouchStrokeCommand.Execute(new RetouchStroke([new RetouchPoint(4, 4)], BrushSize: 3));
        await WaitUntil(() => viewModel.ResultSlot.Result?[4, 4, 0] < 0.75f);

        viewModel.ResultSlot.Result!.Width.Should().Be(width);
        viewModel.ResultSlot.Result.Height.Should().Be(height);
        viewModel.RedSlot.Image![4, 4].Should().BeLessThan(0.75f);
    }

    [Fact]
    public async Task StampAfterAlign_RebuildsPreparedResultWithoutChangingSize()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(12, 12, 0.1f);
        red[2, 2] = 0.9f;
        viewModel.RedSlot.Image = red;
        viewModel.GreenSlot.Image = ImageBuffer.Filled(12, 12, 0.5f);
        viewModel.BlueSlot.Image = ImageBuffer.Filled(12, 12, 0.5f);
        viewModel.AutoWhiteBalance = false;
        await viewModel.AutoAlignCommand.ExecuteAsync(null);
        var width = viewModel.ResultSlot.Result!.Width;
        var height = viewModel.ResultSlot.Result.Height;

        viewModel.SelectedSlot = viewModel.RedSlot;
        viewModel.ApplyStampStrokeCommand.Execute(new CloneStampStroke(
            SourceAnchor: new RetouchPoint(2, 2),
            DestinationStroke: new RetouchStroke([new RetouchPoint(8, 8)], BrushSize: 1),
            BlendWidth: 1));
        await WaitUntil(() => viewModel.ResultSlot.Result?[8, 8, 0] > 0.75f);

        viewModel.ResultSlot.Result!.Width.Should().Be(width);
        viewModel.ResultSlot.Result.Height.Should().Be(height);
        viewModel.RedSlot.Image![8, 8].Should().BeApproximately(0.9f, 0.001f);
    }

    [Fact]
    public async Task CropResult_CropsPreparedChannelsToSameRectangle()
    {
        var viewModel = CreateViewModel();
        SetInputChannels(viewModel, red: 0.25f, green: 0.5f, blue: 0.75f);
        viewModel.AutoWhiteBalance = false;
        await viewModel.AutoAlignCommand.ExecuteAsync(null);

        viewModel.SelectedSlot = viewModel.ResultSlot;
        viewModel.SelectionRect = new ImageSelectionRect(1, 1, 3, 4);
        viewModel.CropToSelectionCommand.Execute(null);

        viewModel.ResultSlot.Result!.Width.Should().Be(3);
        viewModel.ResultSlot.Result.Height.Should().Be(4);
        viewModel.RedSlot.Image!.Width.Should().Be(3);
        viewModel.RedSlot.Image.Height.Should().Be(4);
        viewModel.GreenSlot.Image!.Width.Should().Be(3);
        viewModel.BlueSlot.Image!.Height.Should().Be(4);

        viewModel.RedExposureStops = 1.0;
        await WaitUntil(() => viewModel.ResultSlot.Result?[0, 0, 0] > 0.45f);
        viewModel.ResultSlot.Result!.Width.Should().Be(3);
        viewModel.ResultSlot.Result.Height.Should().Be(4);
    }

    [Fact]
    public async Task ExportChannels_SavesCurrentWorkingChannels()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-channels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var viewModel = new MainViewModel(new FakeFileDialogService { FolderPath = folder });
            SetInputChannels(viewModel, red: 0.25f, green: 0.5f, blue: 0.75f);

            await viewModel.ExportChannelsCommand.ExecuteAsync(null);

            File.Exists(Path.Combine(folder, "red.png")).Should().BeTrue();
            File.Exists(Path.Combine(folder, "green.png")).Should().BeTrue();
            File.Exists(Path.Combine(folder, "blue.png")).Should().BeTrue();
            var red = await ImageLoader.LoadGrayscaleAsync(Path.Combine(folder, "red.png"));
            red[0, 0].Should().BeApproximately(0.25f, 0.01f);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Export_UsesCurrentSettingsAndFormatAwareDialog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-result-{Guid.NewGuid():N}.jpg");
        try
        {
            var fileDialog = new FakeFileDialogService { ExportPath = path };
            var settingsStore = new FakeExportSettingsStore
            {
                Settings = RgbExportSettings.Default with
                {
                    Format = RgbExportFormat.Jpeg,
                    MaxSide = 4,
                    JpegQuality = 80,
                },
            };
            var viewModel = new MainViewModel(fileDialog, settingsStore);
            SetResultWithoutBitmapRefresh(viewModel, FilledRgb(8, 4, 0.5f));

            await viewModel.ExportCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            fileDialog.ExportSettings.Should().NotBeNull();
            fileDialog.ExportSettings!.Format.Should().Be(RgbExportFormat.Jpeg);
            fileDialog.ExportSettings.MaxSide.Should().Be(4);
            File.Exists(path).Should().BeTrue();
            var loaded = await ImageLoader.LoadGrayscaleAsync(path).WaitAsync(TimeSpan.FromSeconds(10));
            loaded.Width.Should().Be(4);
            loaded.Height.Should().Be(2);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ExportSettingsChanges_ArePersisted()
    {
        var settingsStore = new FakeExportSettingsStore();
        var viewModel = new MainViewModel(new FakeFileDialogService(), settingsStore);

        viewModel.ExportFormat = RgbExportFormat.Tiff;
        viewModel.LimitExportSize = true;
        viewModel.ExportMaxSide = 1200;
        viewModel.TiffCompression = TiffExportCompression.Deflate;
        viewModel.TiffDeflateLevel = 8;

        settingsStore.Settings.Should().Be(RgbExportSettings.Default with
        {
            Format = RgbExportFormat.Tiff,
            MaxSide = 1200,
            TiffCompression = TiffExportCompression.Deflate,
            TiffDeflateLevel = 8,
        });
    }

    [Fact]
    public void BrushRetouch_IsUndoable()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(9, 9, 0.5f);
        red[4, 4] = 1.0f;
        viewModel.RedSlot.Image = red;
        viewModel.SelectedSlot = viewModel.RedSlot;

        viewModel.ApplyRetouchStrokeCommand.Execute(new RetouchStroke([new RetouchPoint(4, 4)], BrushSize: 3));

        viewModel.RedSlot.Image![4, 4].Should().BeLessThan(0.75f);
        viewModel.UndoCommand.Execute(null);
        viewModel.RedSlot.Image![4, 4].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void Stamp_IsUndoable()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(12, 12, 0.1f);
        red[2, 2] = 0.9f;
        viewModel.RedSlot.Image = red;
        viewModel.SelectedSlot = viewModel.RedSlot;

        viewModel.ApplyStampStrokeCommand.Execute(new CloneStampStroke(
            SourceAnchor: new RetouchPoint(2, 2),
            DestinationStroke: new RetouchStroke([new RetouchPoint(8, 8)], BrushSize: 1),
            BlendWidth: 1));

        viewModel.RedSlot.Image![8, 8].Should().BeApproximately(0.9f, 0.001f);
        viewModel.UndoCommand.Execute(null);
        viewModel.RedSlot.Image![8, 8].Should().BeApproximately(0.1f, 0.001f);
    }

    [Fact]
    public void EmptyStamp_DoesNotMutateSelectedChannel()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(12, 12, 0.1f);
        viewModel.RedSlot.Image = red;
        viewModel.SelectedSlot = viewModel.RedSlot;

        viewModel.ApplyStampStrokeCommand.Execute(new CloneStampStroke(
            SourceAnchor: new RetouchPoint(2, 2),
            DestinationStroke: new RetouchStroke([], BrushSize: 1),
            BlendWidth: 1));

        viewModel.RedSlot.Image.Should().BeSameAs(red);
        viewModel.UndoCommand.CanExecute(null).Should().BeFalse();
    }

    private static MainViewModel CreateViewModel()
    {
        EnsureAvalonia();
        return new MainViewModel(new FakeFileDialogService());
    }

    private static void EnsureAvalonia()
    {
        lock (AvaloniaLock)
        {
            if (avaloniaInitialized)
            {
                return;
            }

            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
            avaloniaInitialized = true;
        }
    }

    private static void SetInputChannels(MainViewModel viewModel, float red, float green, float blue)
    {
        viewModel.RedSlot.Image = ImageBuffer.Filled(8, 8, red);
        viewModel.GreenSlot.Image = ImageBuffer.Filled(8, 8, green);
        viewModel.BlueSlot.Image = ImageBuffer.Filled(8, 8, blue);
    }

    private static void SetAlignedChannels(
        MainViewModel viewModel,
        ImageBuffer red,
        ImageBuffer green,
        ImageBuffer blue)
    {
        viewModel.RedSlot.Image = red;
        viewModel.GreenSlot.Image = green;
        viewModel.BlueSlot.Image = blue;
        viewModel.ResultSlot.Result = MergeRgb(red, green, blue);
        var aligned = new AlignedChannels(
            red.Clone(),
            green.Clone(),
            blue.Clone(),
            FullMask(red),
            FullMask(green),
            FullMask(blue),
            new Dictionary<ChannelName, AlignChannelMetadata>
            {
                [ChannelName.Red] = new("reference", 0),
                [ChannelName.Green] = new("reference", 0),
                [ChannelName.Blue] = new("reference", 0),
            });

        var field = typeof(MainViewModel).GetField(
            "lastAligned",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(viewModel, aligned);
    }

    private static RgbImageBuffer FilledRgb(int width, int height, float value)
    {
        var pixels = new float[width * height * 3];
        Array.Fill(pixels, value);
        return new RgbImageBuffer(width, height, pixels);
    }

    private static RgbImageBuffer MergeRgb(ImageBuffer red, ImageBuffer green, ImageBuffer blue)
    {
        red.Width.Should().Be(green.Width);
        red.Width.Should().Be(blue.Width);
        red.Height.Should().Be(green.Height);
        red.Height.Should().Be(blue.Height);

        var pixels = new float[red.Width * red.Height * 3];
        for (var i = 0; i < red.Width * red.Height; i++)
        {
            var offset = i * 3;
            pixels[offset] = red.Pixels[i];
            pixels[offset + 1] = green.Pixels[i];
            pixels[offset + 2] = blue.Pixels[i];
        }

        return new RgbImageBuffer(red.Width, red.Height, pixels);
    }

    private static byte[] FullMask(ImageBuffer image)
    {
        var mask = new byte[image.Width * image.Height];
        Array.Fill(mask, (byte)1);
        return mask;
    }

    private static void SetResultWithoutBitmapRefresh(MainViewModel viewModel, RgbImageBuffer result)
    {
        var field = typeof(ChannelSlotViewModel).GetField(
            "result",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(viewModel.ResultSlot, result);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 30; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        condition().Should().BeTrue();
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? FolderPath { get; init; }

        public string? ExportPath { get; init; }

        public RgbExportSettings? ExportSettings { get; private set; }

        public Task<string?> OpenImage()
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> SaveExport(RgbExportSettings settings)
        {
            ExportSettings = settings;
            return Task.FromResult(ExportPath);
        }

        public Task<string?> OpenFolder()
        {
            return Task.FromResult(FolderPath);
        }
    }

    private sealed class FakeExportSettingsStore : IExportSettingsStore
    {
        public RgbExportSettings Settings { get; set; } = RgbExportSettings.Default;

        public RgbExportSettings Load()
        {
            return Settings;
        }

        public void Save(RgbExportSettings settings)
        {
            Settings = settings;
        }
    }
}
