using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Prokudin.Gui.Views;

public sealed partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionTextBlock.Text = version is null
            ? "Version: unknown"
            : $"Version: {version}";
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
