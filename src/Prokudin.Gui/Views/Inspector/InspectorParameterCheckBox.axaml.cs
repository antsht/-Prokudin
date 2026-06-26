using Avalonia;
using Avalonia.Controls;

namespace Prokudin.Gui.Views.Inspector;

public sealed partial class InspectorParameterCheckBox : UserControl
{
    public static readonly StyledProperty<object?> LabelProperty =
        AvaloniaProperty.Register<InspectorParameterCheckBox, object?>(nameof(Label));

    public static readonly StyledProperty<string> ShortTipProperty =
        AvaloniaProperty.Register<InspectorParameterCheckBox, string>(nameof(ShortTip));

    public static readonly StyledProperty<string> LongTipProperty =
        AvaloniaProperty.Register<InspectorParameterCheckBox, string>(nameof(LongTip));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<InspectorParameterCheckBox, bool>(nameof(IsChecked));

    public InspectorParameterCheckBox()
    {
        InitializeComponent();
    }

    public object? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string ShortTip
    {
        get => GetValue(ShortTipProperty);
        set => SetValue(ShortTipProperty, value);
    }

    public string LongTip
    {
        get => GetValue(LongTipProperty);
        set => SetValue(LongTipProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }
}
