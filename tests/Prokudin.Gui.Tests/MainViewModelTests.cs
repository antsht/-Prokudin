using Avalonia;
using FluentAssertions;
using Prokudin.Core.Imaging;
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

        viewModel.RedExposureStops = 1.0;
        await WaitUntil(() => viewModel.ResultSlot.Result?[0, 0, 0] > 0.45f);

        viewModel.ResultSlot.Result![0, 0, 0].Should().BeApproximately(0.5f, 0.01f);
        viewModel.ResultSlot.Result[0, 0, 1].Should().BeApproximately(0.25f, 0.01f);
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
    public async Task AutoCleanAfterAlign_ReusesCachedTransformAndKeepsResult()
    {
        var viewModel = CreateViewModel();
        var red = ImageBuffer.Filled(21, 21, 0.5f);
        red[10, 10] = 1.0f;
        viewModel.RedSlot.Image = red;
        viewModel.GreenSlot.Image = ImageBuffer.Filled(21, 21, 0.5f);
        viewModel.BlueSlot.Image = ImageBuffer.Filled(21, 21, 0.5f);
        viewModel.AutoWhiteBalance = false;
        await viewModel.AutoAlignCommand.ExecuteAsync(null);

        viewModel.SelectedSlot = viewModel.RedSlot;
        await viewModel.AutoCleanSelectedChannelCommand.ExecuteAsync(null);
        await WaitUntil(() => viewModel.ResultSlot.Result is not null);

        viewModel.RedSlot.Image![10, 10].Should().BeLessThan(0.85f);
        viewModel.ResultSlot.Result.Should().NotBeNull();
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
        public Task<string?> OpenImage()
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> SavePng()
        {
            return Task.FromResult<string?>(null);
        }
    }
}
