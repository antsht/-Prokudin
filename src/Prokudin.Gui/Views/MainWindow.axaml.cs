using System.Collections.Specialized;
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
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!await viewModel.TryCloseSessionAsync())
        {
            e.Cancel = true;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ApplyPanelLayout(viewModel);
            RefreshRecentProjectsMenu(viewModel);
            viewModel.RecentProjectsMenu.CollectionChanged += (_, _) => RefreshRecentProjectsMenu(viewModel);
        }
    }

    private void RefreshRecentProjectsMenu(MainViewModel viewModel)
    {
        RecentProjectsMenuItem.Items.Clear();
        foreach (var entry in viewModel.RecentProjectsMenu)
        {
            var item = new MenuItem
            {
                Header = entry.DisplayName ?? entry.Path,
            };
            item.Click += (_, _) => viewModel.OpenRecentProjectCommand.Execute(entry.Path);
            RecentProjectsMenuItem.Items.Add(item);
        }

        if (RecentProjectsMenuItem.Items.Count == 0)
        {
            RecentProjectsMenuItem.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
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
