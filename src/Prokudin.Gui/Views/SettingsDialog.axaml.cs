using Avalonia.Controls;
using Avalonia.Interactivity;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel viewModel)
        {
            viewModel.ApplyCommand.Execute(this);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
