using Avalonia.Controls;
using Avalonia.Interactivity;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is WelcomeViewModel viewModel && !viewModel.WaitForChoiceAsync().IsCompleted)
        {
            viewModel.Cancel();
        }
    }

    private void RecentList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WelcomeViewModel viewModel || RecentList.SelectedItem is not RecentProjectEntry entry)
        {
            return;
        }

        viewModel.OpenRecentCommand.Execute(entry.Path);
        RecentList.SelectedItem = null;
    }

    private async void Settings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WelcomeViewModel viewModel)
        {
            await viewModel.OpenSettingsCommand.ExecuteAsync(this);
        }
    }
}
