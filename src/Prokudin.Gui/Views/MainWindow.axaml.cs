using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class MainWindow : Window
{
    private bool stickLogToBottom = true;
    private MainViewModel? viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = DataContext as MainViewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ProcessingLogText) || !stickLogToBottom)
        {
            return;
        }

        ScrollLogToEnd();
    }

    private void ProcessingLogScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var maxOffset = Math.Max(0, ProcessingLogScrollViewer.Extent.Height - ProcessingLogScrollViewer.Viewport.Height);
        stickLogToBottom = ProcessingLogScrollViewer.Offset.Y >= maxOffset - 4.0;
    }

    private void ScrollLogToEnd()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                ProcessingLogScrollViewer.ScrollToEnd();
            },
            DispatcherPriority.Loaded);
    }
}
