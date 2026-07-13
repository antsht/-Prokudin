using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        SizeChanged += OnSizeChanged;
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
            viewModel.SetWorkspaceWidth(Bounds.Width);
            RefreshRecentProjectsMenu(viewModel);
            viewModel.RecentProjectsMenu.CollectionChanged += (_, _) => RefreshRecentProjectsMenu(viewModel);
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            AddHandler(InputElement.GotFocusEvent, OnInputFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(InputElement.LostFocusEvent, OnInputFocusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetWorkspaceWidth(e.NewSize.Width);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.IsLeftPanelEffectivelyVisible)
            or nameof(MainViewModel.IsRightInspectorVisible)
            or nameof(MainViewModel.IsProcessingLogVisible))
        {
            ApplyPanelLayout(viewModel);
        }
    }

    private void OnInputFocusChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.NotifyKeyboardShortcutCommandsChanged();
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
        WorkspaceGrid.ColumnDefinitions[0].Width = viewModel.IsLeftPanelEffectivelyVisible
            ? new GridLength(viewModel.LeftPanelWidthClamped)
            : new GridLength(0);
        WorkspaceGrid.ColumnDefinitions[1].Width = viewModel.IsLeftPanelEffectivelyVisible
            ? new GridLength(4)
            : new GridLength(0);
        WorkspaceGrid.ColumnDefinitions[5].Width = viewModel.IsRightInspectorVisible
            ? new GridLength(viewModel.RightInspectorWidthClamped)
            : new GridLength(0);
        WorkspaceGrid.ColumnDefinitions[4].Width = viewModel.IsRightInspectorVisible
            ? new GridLength(4)
            : new GridLength(0);
        RootGrid.RowDefinitions[3].Height = viewModel.IsProcessingLogVisible
            ? new GridLength(4)
            : new GridLength(0);
        RootGrid.RowDefinitions[4].Height = viewModel.IsProcessingLogVisible
            ? new GridLength(viewModel.ProcessingLogHeightClamped)
            : new GridLength(0);
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
