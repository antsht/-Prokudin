using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Services;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task ExposureEdit_IsUndoableAndRedoable()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() => LoadSyntheticChannels(viewModel));

        AvaloniaTestHost.Invoke(() => viewModel.RedExposureStops = 1.0);
        await Task.Delay(800);

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.UndoCommand.CanExecute(null).Should().BeTrue();
            viewModel.UndoCommand.Execute(null);
            viewModel.RedExposureStops.Should().Be(0.0);

            viewModel.RedoCommand.CanExecute(null).Should().BeTrue();
            viewModel.RedoCommand.Execute(null);
            viewModel.RedExposureStops.Should().Be(1.0);
        });
    }

    [Fact]
    public async Task ParameterUndo_RecomputesResultFromAlignedChannels()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.AlignMaxTranslation = 12;
        });

        var alignTask = AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null));
        await alignTask!;

        AvaloniaTestHost.Invoke(() => viewModel.RedExposureStops = 0.5);
        await Task.Delay(800);
        var adjusted = AvaloniaTestHost.Invoke(() => viewModel.ResultSlot.Result!.Clone());
        var adjustedMean = MeanRgb(adjusted);

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.UndoCommand.Execute(null);
            viewModel.RedExposureStops.Should().Be(0.0);
            viewModel.ResultSlot.Result.Should().NotBeNull();
            viewModel.ResultSlot.Result!.Width.Should().Be(adjusted.Width);
            viewModel.ResultSlot.Result.Height.Should().Be(adjusted.Height);
            Math.Abs(MeanRgb(viewModel.ResultSlot.Result) - adjustedMean).Should().BeGreaterThan(0.001f);

            viewModel.RedoCommand.Execute(null);
            viewModel.RedExposureStops.Should().Be(0.5);
            viewModel.ResultSlot.Result.Should().NotBeNull();
            MeanAbsoluteDifference(adjusted, viewModel.ResultSlot.Result!).Should().BeLessThan(1e-6f);
        });
    }

    [Fact]
    public void SwapSlots_IsUndoable()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            var red = ImageBuffer.Filled(8, 8, 0.2f);
            var green = ImageBuffer.Filled(8, 8, 0.8f);
            viewModel.RedSlot.Image = red;
            viewModel.GreenSlot.Image = green;

            viewModel.SwapSlots(viewModel.RedSlot, viewModel.GreenSlot);
            viewModel.RedSlot.Image.Should().BeSameAs(green);
            viewModel.GreenSlot.Image.Should().BeSameAs(red);

            viewModel.UndoCommand.Execute(null);
            viewModel.RedSlot.Image![0, 0].Should().BeApproximately(red[0, 0], 1e-6f);
            viewModel.GreenSlot.Image![0, 0].Should().BeApproximately(green[0, 0], 1e-6f);
        });
    }

    [Fact]
    public async Task AutoAlign_IsUndoable()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.AlignMaxTranslation = 12;
        });

        var redBefore = AvaloniaTestHost.Invoke(() => viewModel.RedSlot.Image!.Clone());
        var alignTask = AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null));
        await alignTask!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.ResultSlot.Result.Should().NotBeNull();
            viewModel.UndoCommand.CanExecute(null).Should().BeTrue();
            viewModel.UndoCommand.Execute(null);

            viewModel.ResultSlot.Result.Should().BeNull();
            viewModel.RedSlot.Image![0, 0]
                .Should()
                .BeApproximately(redBefore[0, 0], 1e-6f);
        });
    }

    [Fact]
    public void CropToSelection_IsUndoable()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            var image = ImageBuffer.Filled(64, 64, 0.5f);
            viewModel.GreenSlot.Image = image;
            viewModel.SelectedSlot = viewModel.GreenSlot;
            viewModel.SelectionRect = new ImageSelectionRect(10, 10, 20, 20);

            viewModel.CropToSelectionCommand.Execute(null);
            viewModel.GreenSlot.Image!.Width.Should().Be(20);
            viewModel.GreenSlot.Image.Height.Should().Be(20);

            viewModel.UndoCommand.Execute(null);
            viewModel.GreenSlot.Image!.Width.Should().Be(64);
            viewModel.GreenSlot.Image.Height.Should().Be(64);
        });
    }

    [Fact]
    public async Task OpenRed_IsUndoable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prokudin-red-{Guid.NewGuid():N}.png");
        try
        {
            var source = ImageBuffer.Filled(16, 16, 0.42f);
            await ImageLoader.SaveGrayscalePngAsync(path, source);

            var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel(new FakeFileDialogService { ImagePath = path }));
            var openTask = AvaloniaTestHost.Invoke(() => viewModel.OpenRedCommand.ExecuteAsync(null));
            await openTask!;

            AvaloniaTestHost.Invoke(() =>
            {
                viewModel.RedSlot.Image.Should().NotBeNull();
                viewModel.UndoCommand.Execute(null);
                viewModel.RedSlot.Image.Should().BeNull();
            });
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
    public async Task BrushRetouch_IsUndoable()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.SelectedSlot = viewModel.GreenSlot;
            viewModel.UseCrossChannelHealing = false;
            viewModel.UseTeleaHealing = true;
        });

        var before = AvaloniaTestHost.Invoke(() => viewModel.GreenSlot.Image!.Clone());
        var stroke = new RetouchStroke([new RetouchPoint(64, 64)], BrushSize: 16);
        var retouchTask = AvaloniaTestHost.Invoke(() => viewModel.ApplyRetouchStrokeCommand.ExecuteAsync(stroke));
        await retouchTask!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.GreenSlot.Image.Should().NotBeSameAs(before);
            viewModel.UndoCommand.Execute(null);
            viewModel.GreenSlot.Image![64, 64]
                .Should()
                .BeApproximately(before[64, 64], 1e-6f);
        });
    }

    [Fact]
    public async Task AutoAlign_ResultAndPreviewDisplayBitmap_AreVisible()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.AlignMaxTranslation = 12;
        });

        await AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null))!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.ResultSlot.Result.Should().NotBeNull();
            viewModel.ResultSlot.DisplayBitmap.Should().NotBeNull();
            viewModel.PreviewDisplayBitmap.Should().NotBeNull();
            MeanBitmapGray(viewModel.ResultSlot.DisplayBitmap!).Should().BeGreaterThan(15.0,
                "RGB result bitmap should show aligned content");
            MeanBitmapGray(viewModel.PreviewDisplayBitmap!).Should().BeGreaterThan(15.0,
                "preview should bind to the visible result bitmap after align");
        });
    }

    [Fact]
    public async Task AutoAlign_LargeTriptych_DoesNotCorruptResult()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            var triptych = ImageBuffer.Filled(6000, 2000, 0.0f);
            for (var segment = 0; segment < 3; segment++)
            {
                var value = 0.2f + (segment * 0.25f);
                for (var y = 400; y < 1600; y++)
                {
                    for (var x = (segment * 2000) + 200; x < ((segment + 1) * 2000) - 200; x++)
                    {
                        triptych[x, y] = value;
                    }
                }
            }

            var channels = TriptychSplitter.SplitTriptych(triptych, TriptychOrder.Bgr, trimBlackBorders: false);
            viewModel.RedSlot.Image = channels[ChannelName.Red];
            viewModel.GreenSlot.Image = channels[ChannelName.Green];
            viewModel.BlueSlot.Image = channels[ChannelName.Blue];
            viewModel.AlignMaxTranslation = 64;
        });

        await AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null))!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.ResultSlot.Result.Should().NotBeNull();
            viewModel.ResultSlot.DisplayBitmap.Should().NotBeNull();
            viewModel.ResultSlot.DisplayBitmap!.PixelSize.Width.Should().BePositive();
            MeanNormalized(viewModel.RedSlot.Image!).Should().BeGreaterThan(0.01f);
        });
    }

    [Fact]
    public async Task BrushRetouch_AfterAutoAlign_DoesNotBlackenChannel()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.AlignMaxTranslation = 12;
            viewModel.SelectedSlot = viewModel.GreenSlot;
            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
        });

        await AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null))!;

        var meanBefore = AvaloniaTestHost.Invoke(() => MeanNormalized(viewModel.GreenSlot.Image!));
        var stroke = new RetouchStroke([new RetouchPoint(64, 64)], BrushSize: 16);
        await AvaloniaTestHost.Invoke(() => viewModel.ApplyRetouchStrokeCommand.ExecuteAsync(stroke))!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.GreenSlot.Image.Should().NotBeNull();
            MeanNormalized(viewModel.GreenSlot.Image!).Should().BeGreaterThan(0.05f, "heal should not blacken the channel");
            MeanNormalized(viewModel.GreenSlot.Image!).Should().BeApproximately(meanBefore, 0.15f);
        });
    }

    [Fact]
    public async Task BrushRetouch_AfterAutoAlign_DisplayBitmap_IsNotBlack()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.AlignMaxTranslation = 12;
            viewModel.SelectedSlot = viewModel.GreenSlot;
            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
        });

        await AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null))!;
        AvaloniaTestHost.Invoke(() => viewModel.SelectedSlot = viewModel.GreenSlot);

        var stroke = new RetouchStroke([new RetouchPoint(64, 64)], BrushSize: 16);
        await AvaloniaTestHost.Invoke(() => viewModel.ApplyRetouchStrokeCommand.ExecuteAsync(stroke))!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.GreenSlot.DisplayBitmap.Should().NotBeNull();
            var imageMeanGray = MeanNormalized(viewModel.GreenSlot.Image!) * 255.0;
            MeanBitmapGray(viewModel.GreenSlot.DisplayBitmap!).Should().BeGreaterThan(imageMeanGray * 0.5,
                "preview bitmap should track channel pixels after heal");
            MeanBitmapGray(viewModel.PreviewDisplayBitmap!).Should().BeGreaterThan(imageMeanGray * 0.5,
                "bound preview bitmap should track channel pixels after heal");
        });
    }

    [Fact]
    public async Task BrushRetouch_WithoutAlign_DisplayBitmap_IsNotBlack()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannels(viewModel);
            viewModel.SelectedSlot = viewModel.GreenSlot;
            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
        });

        var stroke = new RetouchStroke([new RetouchPoint(64, 64)], BrushSize: 16);
        await AvaloniaTestHost.Invoke(() => viewModel.ApplyRetouchStrokeCommand.ExecuteAsync(stroke))!;

        AvaloniaTestHost.Invoke(() =>
        {
            var imageMeanGray = MeanNormalized(viewModel.GreenSlot.Image!) * 255.0;
            MeanBitmapGray(viewModel.GreenSlot.DisplayBitmap!).Should().BeGreaterThan(imageMeanGray * 0.5);
            MeanBitmapGray(viewModel.PreviewDisplayBitmap!).Should().BeGreaterThan(imageMeanGray * 0.5);
        });
    }

    [Fact]
    public async Task BrushRetouch_AfterAutoAlign_UInt16_DoesNotBlackenChannel()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() =>
        {
            LoadSyntheticChannelsUInt16(viewModel);
            viewModel.AlignMaxTranslation = 12;
            viewModel.SelectedSlot = viewModel.GreenSlot;
            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
        });

        await AvaloniaTestHost.Invoke(() => viewModel.AutoAlignCommand.ExecuteAsync(null))!;
        AvaloniaTestHost.Invoke(() => viewModel.SelectedSlot = viewModel.GreenSlot);

        var stroke = new RetouchStroke([new RetouchPoint(64, 64)], BrushSize: 16);
        await AvaloniaTestHost.Invoke(() => viewModel.ApplyRetouchStrokeCommand.ExecuteAsync(stroke))!;

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.GreenSlot.DisplayBitmap.Should().NotBeNull();
            var imageMeanGray = MeanNormalized(viewModel.GreenSlot.Image!) * 255.0;
            imageMeanGray.Should().BeGreaterThan(12.0, "heal should not blacken the channel");
            MeanBitmapGray(viewModel.GreenSlot.DisplayBitmap!).Should().BeGreaterThan(imageMeanGray * 0.5,
                "preview bitmap should track channel pixels after heal");
            MeanBitmapGray(viewModel.PreviewDisplayBitmap!).Should().BeGreaterThan(imageMeanGray * 0.5,
                "bound preview bitmap should track channel pixels after heal");
        });
    }

    [Fact]
    public void Stamp_IsUndoable()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            var image = ImageBuffer.Filled(12, 8, 0.1f);
            image[2, 3] = 0.9f;
            viewModel.GreenSlot.Image = image;
            viewModel.SelectedSlot = viewModel.GreenSlot;

            var before = image[8, 4];
            var stroke = new CloneStampStroke(
                new RetouchPoint(2, 3),
                new RetouchStroke([new RetouchPoint(8, 4)], BrushSize: 1),
                BlendWidth: 1);
            viewModel.ApplyStampStrokeCommand.Execute(stroke);

            viewModel.GreenSlot.Image![8, 4].Should().BeApproximately(0.9f, 1e-3f);
            viewModel.UndoCommand.Execute(null);
            viewModel.GreenSlot.Image![8, 4].Should().BeApproximately(before, 1e-6f);
        });
    }

    [Fact]
    public void LevelsEdit_IsUndoable()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            LoadSyntheticChannels(viewModel);
            viewModel.LevelsBlackPoint = 0.1;

            viewModel.UndoCommand.Execute(null);
            viewModel.LevelsBlackPoint.Should().Be(0.0);
        });
    }

    [Fact]
    public async Task CoalescedColorEdit_MultipleChangesProduceSingleUndo()
    {
        var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
        AvaloniaTestHost.Invoke(() => LoadSyntheticChannels(viewModel));

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.RedExposureStops = 0.5;
            viewModel.GreenExposureStops = 0.25;
        });

        await Task.Delay(100);

        AvaloniaTestHost.Invoke(() =>
        {
            viewModel.UndoCommand.Execute(null);
            viewModel.RedExposureStops.Should().Be(0.0);
            viewModel.GreenExposureStops.Should().Be(0.0);
            viewModel.UndoCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public async Task LoadProject_RestoresPreparedStateForAutoClean()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-autoclean-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var channel = ImageBuffer.Filled(32, 32, 0.5f);
            var store = new JsonProjectStore();
            var package = new ProjectPackage
            {
                Document = ProjectStateMapper.ToDocument(
                    new ProjectCapture
                    {
                        TriptychOrder = "BGR",
                        AlignDetector = "sift",
                    },
                    includeExportOverride: false),
                Red = channel.Clone(),
                Green = channel.Clone(),
                Blue = channel.Clone(),
                Result = new RgbImageBuffer(32, 32, Enumerable.Repeat(0.5f, 32 * 32 * 3).ToArray()),
            };
            await store.SaveAsync(folder, package);

            var viewModel = AvaloniaTestHost.Invoke(() => CreateViewModel());
            await viewModel.CompleteStartupAsync(new StartupChoice
            {
                Type = StartupChoiceType.OpenRecent,
                ProjectPath = folder,
            });

            AvaloniaTestHost.Invoke(() =>
            {
                viewModel.SelectedSlot = viewModel.RedSlot;
                viewModel.RedSlot.State.Should().Be(ChannelSlotState.Aligned);
                viewModel.AutoCleanSelectedChannelCommand.CanExecute(null).Should().BeTrue();
            });
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static MainViewModel CreateViewModel(IFileDialogService? fileDialogService = null)
    {
        var settingsDir = Path.Combine(Path.GetTempPath(), $"prokudin-test-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(settingsDir);
        return new MainViewModel(
            fileDialogService ?? new FakeFileDialogService(),
            new JsonExportSettingsStore(Path.Combine(settingsDir, "export.json")),
            new JsonProcessingDiagnosticsSettingsStore(Path.Combine(settingsDir, "diagnostics.json")),
            new JsonAutoCleanSettingsStore(Path.Combine(settingsDir, "auto-clean.json")));
    }

    private static void LoadSyntheticChannels(MainViewModel viewModel)
    {
        var green = SyntheticChannel();
        var red = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: 6, dy: -4).Image;
        var blue = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: -5, dy: 7).Image;
        viewModel.RedSlot.Image = red;
        viewModel.GreenSlot.Image = green;
        viewModel.BlueSlot.Image = blue;
    }

    private static void LoadSyntheticChannelsUInt16(MainViewModel viewModel)
    {
        var green = SyntheticChannelUInt16();
        var red = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: 6, dy: -4).Image;
        var blue = ChannelAligner.WarpTranslation(green, green.Width, green.Height, dx: -5, dy: 7).Image;
        viewModel.RedSlot.Image = red;
        viewModel.GreenSlot.Image = green;
        viewModel.BlueSlot.Image = blue;
    }

    private static float MeanNormalized(ImageBuffer image)
    {
        var sum = 0.0f;
        for (var i = 0; i < image.PixelCount; i++)
        {
            sum += image.GetNormalized(i);
        }

        return sum / image.PixelCount;
    }

    private static float MeanRgb(RgbImageBuffer image)
    {
        var sum = 0.0f;
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            sum += image.Pixels[i];
        }

        return sum / image.Pixels.Length;
    }

    private static float MeanAbsoluteDifference(RgbImageBuffer left, RgbImageBuffer right)
    {
        left.Width.Should().Be(right.Width);
        left.Height.Should().Be(right.Height);
        var sum = 0.0f;
        for (var i = 0; i < left.Pixels.Length; i++)
        {
            sum += Math.Abs(left.Pixels[i] - right.Pixels[i]);
        }

        return sum / left.Pixels.Length;
    }

    private static double MeanBitmapGray(Bitmap bitmap)
    {
        if (bitmap is not WriteableBitmap writeable)
        {
            throw new InvalidOperationException("Expected WriteableBitmap.");
        }

        using var buffer = writeable.Lock();
        var height = writeable.PixelSize.Height;
        var width = writeable.PixelSize.Width;
        var rowBytes = buffer.RowBytes;
        var bytes = new byte[rowBytes * height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);

        double sum = 0;
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                sum += bytes[offset];
                count++;
            }
        }

        return sum / count;
    }

    private static ImageBuffer SyntheticChannelUInt16()
    {
        var image = ImageBuffer.Filled(128, 128, 0.0f, PixelFormat.UInt16);
        for (var y = 20; y < 108; y++)
        {
            for (var x = 24; x < 104; x++)
            {
                image[x, y] = 0.25f;
            }
        }

        for (var y = 48; y < 80; y++)
        {
            for (var x = 54; x < 86; x++)
            {
                image[x, y] = 0.85f;
            }
        }

        return image;
    }

    private static ImageBuffer SyntheticChannel()
    {
        var image = ImageBuffer.Filled(128, 128, 0.0f);
        for (var y = 20; y < 108; y++)
        {
            for (var x = 24; x < 104; x++)
            {
                image[x, y] = 0.25f;
            }
        }

        for (var y = 48; y < 80; y++)
        {
            for (var x = 54; x < 86; x++)
            {
                image[x, y] = 0.85f;
            }
        }

        return image;
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? ImagePath { get; init; }

        public string? FolderPath { get; init; }

        public string? ExportPath { get; init; }

        public Task<string?> OpenImage() => Task.FromResult(ImagePath);

        public Task<string?> OpenFolder() => Task.FromResult(FolderPath);

        public Task<string?> OpenProjectFolder() => Task.FromResult<string?>(null);

        public Task<string?> PickProjectSaveFolder(string? suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> SaveExport(RgbExportSettings settings) => Task.FromResult(ExportPath);
    }
}
