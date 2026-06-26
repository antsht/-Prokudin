using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Prokudin.Gui.Views.Inspector;

public static class InspectorTipProperties
{
    public static readonly AttachedProperty<string?> LongHelpProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("LongHelp", typeof(InspectorTipProperties));

    public static void SetLongHelp(Control element, string? value) =>
        element.SetValue(LongHelpProperty, value);

    public static string? GetLongHelp(Control element) =>
        element.GetValue(LongHelpProperty);

    static InspectorTipProperties()
    {
        LongHelpProperty.Changed.AddClassHandler<Control>(OnLongHelpChanged);
    }

    private static void OnLongHelpChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        AutomationProperties.SetHelpText(control, e.NewValue as string);
    }
}
