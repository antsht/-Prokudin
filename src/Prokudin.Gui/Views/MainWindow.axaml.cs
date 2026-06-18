using Avalonia.Controls;
using Avalonia.Input;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class MainWindow : Window
{
    private const string SlotFormat = "application/x-prokudin-channel-slot";

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void SlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ChannelSlotViewModel { CanSwap: true } slot })
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        var data = new DataObject();
        data.Set(SlotFormat, slot.DisplayName);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void SlotDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            sender is not Control { DataContext: ChannelSlotViewModel target } ||
            e.Data.Get(SlotFormat) is not string sourceName)
        {
            return;
        }

        var source = viewModel.Slots.FirstOrDefault(slot => slot.DisplayName == sourceName);
        if (source is null)
        {
            return;
        }

        viewModel.SwapSlots(source, target);
        e.DragEffects = DragDropEffects.Move;
    }
}
