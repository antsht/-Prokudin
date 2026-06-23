using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Prokudin.Gui.Views;

public sealed partial class MainWindow : Window
{
    private bool stickLogToBottom = true;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ProcessingLogTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!stickLogToBottom)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => ProcessingLogTextBox.FindDescendantOfType<ScrollViewer>()?.ScrollToEnd(),
            DispatcherPriority.Background);
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!IsPointerOverLogTextBox(e.Source))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (ProcessingLogTextBox.FindDescendantOfType<ScrollViewer>() is not { } scrollViewer)
                {
                    return;
                }

                var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                stickLogToBottom = scrollViewer.Offset.Y >= maxOffset - 4.0;
            },
            DispatcherPriority.Loaded);
    }

    private bool IsPointerOverLogTextBox(object? source)
    {
        if (source is not Avalonia.Visual visual)
        {
            return false;
        }

        var current = visual;
        while (current is not null)
        {
            if (ReferenceEquals(current, ProcessingLogTextBox))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }
}
