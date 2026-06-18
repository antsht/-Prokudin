using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class ChannelSlotCard : UserControl
{
    private static readonly DataFormat<string> SlotFormat =
        DataFormat.CreateInProcessFormat<string>("prokudin.channel-slot");

    public ChannelSlotCard()
    {
        InitializeComponent();
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ChannelSlotViewModel { CanSwap: true } slot)
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        var item = new DataTransferItem();
        item.Set(SlotFormat, slot.DisplayName);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (this.FindAncestorOfType<Window>()?.DataContext is not MainViewModel viewModel ||
            DataContext is not ChannelSlotViewModel target ||
            e.DataTransfer is not { } transfer)
        {
            return;
        }

        var sourceName = transfer.TryGetValue(SlotFormat);
        if (sourceName is null)
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
