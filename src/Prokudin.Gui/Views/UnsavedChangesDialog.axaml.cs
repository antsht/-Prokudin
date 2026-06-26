using Avalonia.Controls;
using Avalonia.Interactivity;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesResult Result { get; private set; } = UnsavedChangesResult.Cancel;

    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Result = UnsavedChangesResult.Save;
        Close();
    }

    private void OnDontSave(object? sender, RoutedEventArgs e)
    {
        Result = UnsavedChangesResult.DontSave;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = UnsavedChangesResult.Cancel;
        Close();
    }
}
