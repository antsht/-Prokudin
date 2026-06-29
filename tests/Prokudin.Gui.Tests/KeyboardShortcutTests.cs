using FluentAssertions;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Services;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Tests;

public sealed class KeyboardShortcutTests
{
    [Fact]
    public void SelectGreenSlotShortcut_SelectsGreenSlot()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedSlot = viewModel.RedSlot;

            viewModel.SelectGreenSlotShortcutCommand.Execute(null);

            viewModel.SelectedSlot.Should().Be(viewModel.GreenSlot);
        });
    }

    [Fact]
    public void SelectBlueSlotShortcut_SelectsEmptySlot()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.SelectBlueSlotShortcutCommand.Execute(null);

            viewModel.SelectedSlot.Should().Be(viewModel.BlueSlot);
            viewModel.BlueSlot.HasImage.Should().BeFalse();
        });
    }

    [Fact]
    public void ActivateHealToolShortcut_FromImport_SwitchesToCleanAndHeal()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedWorkflowTool = WorkflowTool.Import;

            viewModel.ActivateHealToolShortcutCommand.Execute(null);

            viewModel.SelectedWorkflowTool.Should().Be(WorkflowTool.Clean);
            viewModel.ToolMode.Should().Be(EditorToolMode.Heal);
        });
    }

    [Fact]
    public void ActivateSelectionToolShortcut_FromClean_KeepsCleanAndSelectsSelection()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
            viewModel.ToolMode = EditorToolMode.Heal;

            viewModel.ActivateSelectionToolShortcutCommand.Execute(null);

            viewModel.SelectedWorkflowTool.Should().Be(WorkflowTool.Clean);
            viewModel.ToolMode.Should().Be(EditorToolMode.Select);
        });
    }

    [Fact]
    public void ActivateSelectionToolShortcut_FromAlign_SwitchesToCropAndSelection()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedWorkflowTool = WorkflowTool.Align;

            viewModel.ActivateSelectionToolShortcutCommand.Execute(null);

            viewModel.SelectedWorkflowTool.Should().Be(WorkflowTool.Crop);
            viewModel.ToolMode.Should().Be(EditorToolMode.Select);
        });
    }

    [Fact]
    public void EditorToolShortcuts_AreBlockedWhenBusy()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.IsBusy = true;

            viewModel.ActivateHealToolShortcutCommand.CanExecute(null).Should().BeFalse();
            viewModel.ActivateCloneToolShortcutCommand.CanExecute(null).Should().BeFalse();
            viewModel.ActivateSelectionToolShortcutCommand.CanExecute(null).Should().BeFalse();

            viewModel.SelectRedSlotShortcutCommand.CanExecute(null).Should().BeTrue();
            viewModel.SelectResultSlotShortcutCommand.CanExecute(null).Should().BeTrue();
        });
    }

    [Fact]
    public void EditorToolShortcuts_AreBlockedWhenAutoCleanMaskPending()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            LoadSyntheticChannels(viewModel);
            viewModel.SelectedSlot = viewModel.RedSlot;
            viewModel.PendingAutoCleanMask = new byte[viewModel.RedSlot.Image!.Width * viewModel.RedSlot.Image.Height];
            viewModel.PendingAutoCleanChannel = ChannelName.Red;

            viewModel.ActivateHealToolShortcutCommand.CanExecute(null).Should().BeFalse();
            viewModel.SelectGreenSlotShortcutCommand.CanExecute(null).Should().BeTrue();
        });
    }

    [Fact]
    public void BrushSizeShortcuts_ExecuteOnlyInCleanHealMode()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.BrushSize = 10;
            viewModel.SelectedWorkflowTool = WorkflowTool.Import;
            viewModel.DecreaseBrushSizeShortcutCommand.CanExecute(null).Should().BeFalse();

            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
            viewModel.ToolMode = EditorToolMode.Heal;
            viewModel.DecreaseBrushSizeShortcutCommand.Execute(null);
            viewModel.BrushSize.Should().Be(9);
        });
    }

    [Fact]
    public void BrushSizeShortcuts_AreBlockedWhenAutoCleanMaskPending()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            LoadSyntheticChannels(viewModel);
            viewModel.SelectedWorkflowTool = WorkflowTool.Clean;
            viewModel.ToolMode = EditorToolMode.Heal;
            viewModel.PendingAutoCleanMask = new byte[viewModel.RedSlot.Image!.Width * viewModel.RedSlot.Image.Height];
            viewModel.PendingAutoCleanChannel = ChannelName.Red;

            viewModel.IncreaseBrushSizeShortcutCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void ManualNudge_ResetClearsPendingShift()
    {
        AvaloniaTestHost.Invoke(() =>
        {
            var viewModel = CreateViewModel();
            viewModel.ManualNudgeBlueDx = 2;
            viewModel.HasUncommittedManualNudge.Should().BeTrue();

            viewModel.ResetManualNudgeCommand.Execute(null);

            viewModel.HasUncommittedManualNudge.Should().BeFalse();
        });
    }

    private static MainViewModel CreateViewModel() => new(new FakeFileDialogService());

    private static void LoadSyntheticChannels(MainViewModel viewModel)
    {
        var image = ImageBuffer.Filled(32, 32, 0.5f);
        viewModel.RedSlot.Image = image;
        viewModel.GreenSlot.Image = image.Clone();
        viewModel.BlueSlot.Image = image.Clone();
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public Task<string?> OpenImage() => Task.FromResult<string?>(null);

        public Task<string?> OpenFolder() => Task.FromResult<string?>(null);

        public Task<string?> OpenProjectFolder() => Task.FromResult<string?>(null);

        public Task<string?> PickProjectSaveFolder(string? suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> SaveExport(RgbExportSettings settings) => Task.FromResult<string?>(null);
    }
}
