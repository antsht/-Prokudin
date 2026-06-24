using Avalonia;
using Avalonia.Styling;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Services;

public static class ThemeService
{
    public static void Apply(AppThemeMode mode)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
