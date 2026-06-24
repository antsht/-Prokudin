using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ApplyPanelLayout(viewModel);
        }
    }

    private void OnLeftColumnSplitterDragCompleted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.LeftPanelWidth = GetColumnWidth(WorkspaceGrid, 0);
    }

    private void OnRightColumnSplitterDragCompleted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.RightInspectorWidth = GetColumnWidth(WorkspaceGrid, 5);
    }

    private void OnLogRowSplitterDragCompleted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.ProcessingLogHeight = GetRowHeight(RootGrid, 4);
    }

    private void ApplyPanelLayout(MainViewModel viewModel)
    {
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(viewModel.LeftPanelWidthClamped);
        WorkspaceGrid.ColumnDefinitions[5].Width = new GridLength(viewModel.RightInspectorWidthClamped);
        RootGrid.RowDefinitions[4].Height = new GridLength(viewModel.ProcessingLogHeightClamped);
    }

    private static double GetColumnWidth(Grid grid, int columnIndex)
    {
        var definition = grid.ColumnDefinitions[columnIndex];
        return definition.Width.IsAbsolute
            ? definition.Width.Value
            : definition.ActualWidth;
    }

    private static double GetRowHeight(Grid grid, int rowIndex)
    {
        var definition = grid.RowDefinitions[rowIndex];
        return definition.Height.IsAbsolute
            ? definition.Height.Value
            : definition.ActualHeight;
    }
}
