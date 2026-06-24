using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Prokudin.Gui.Views;

public sealed partial class KeyboardShortcutsDialog : Window
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
