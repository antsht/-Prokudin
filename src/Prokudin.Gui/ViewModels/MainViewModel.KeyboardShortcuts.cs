using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    private bool CanExecuteSlotShortcut() => !IsTextInputFocused();

    private bool CanExecuteEditorToolShortcut() =>
        !IsBusy && !IsAutoCleanMaskPending && !IsTextInputFocused();

    [RelayCommand(CanExecute = nameof(CanExecuteSlotShortcut))]
    private void SelectRedSlotShortcut() => SelectSlotShortcut(0);

    [RelayCommand(CanExecute = nameof(CanExecuteSlotShortcut))]
    private void SelectGreenSlotShortcut() => SelectSlotShortcut(1);

    [RelayCommand(CanExecute = nameof(CanExecuteSlotShortcut))]
    private void SelectBlueSlotShortcut() => SelectSlotShortcut(2);

    [RelayCommand(CanExecute = nameof(CanExecuteSlotShortcut))]
    private void SelectResultSlotShortcut() => SelectSlotShortcut(3);

    [RelayCommand(CanExecute = nameof(CanExecuteEditorToolShortcut))]
    private void ActivateHealToolShortcut() => ActivateEditorToolShortcut(EditorToolMode.Heal);

    [RelayCommand(CanExecute = nameof(CanExecuteEditorToolShortcut))]
    private void ActivateCloneToolShortcut() => ActivateEditorToolShortcut(EditorToolMode.Clone);

    [RelayCommand(CanExecute = nameof(CanExecuteEditorToolShortcut))]
    private void ActivateSelectionToolShortcut() => ActivateEditorToolShortcut(EditorToolMode.Select);

    private void SelectSlotShortcut(int index)
    {
        if (index < 0 || index >= Slots.Count)
        {
            return;
        }

        SelectedSlot = Slots[index];
    }

    private void ActivateEditorToolShortcut(EditorToolMode mode)
    {
        switch (mode)
        {
            case EditorToolMode.Heal:
            case EditorToolMode.Clone:
                if (SelectedWorkflowTool != WorkflowTool.Clean)
                {
                    SelectedWorkflowTool = WorkflowTool.Clean;
                }

                break;
            case EditorToolMode.Select:
                if (SelectedWorkflowTool != WorkflowTool.Clean)
                {
                    SelectedWorkflowTool = WorkflowTool.Crop;
                }

                break;
            default:
                return;
        }

        SelectTool(mode);
    }

    internal void NotifyKeyboardShortcutCommandsChanged()
    {
        SelectRedSlotShortcutCommand.NotifyCanExecuteChanged();
        SelectGreenSlotShortcutCommand.NotifyCanExecuteChanged();
        SelectBlueSlotShortcutCommand.NotifyCanExecuteChanged();
        SelectResultSlotShortcutCommand.NotifyCanExecuteChanged();
        ActivateHealToolShortcutCommand.NotifyCanExecuteChanged();
        ActivateCloneToolShortcutCommand.NotifyCanExecuteChanged();
        ActivateSelectionToolShortcutCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyKeyboardShortcutCommandsChanged();

    private static bool IsTextInputFocused()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return false;
        }

        var focused = window.FocusManager?.GetFocusedElement();
        return focused is not null && IsWithinTextInputControl(focused);
    }

    private static bool IsWithinTextInputControl(IInputElement element)
    {
        if (element is TextBox or NumericUpDown)
        {
            return true;
        }

        if (element is not Visual visual)
        {
            return false;
        }

        return visual.GetVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown);
    }
}
