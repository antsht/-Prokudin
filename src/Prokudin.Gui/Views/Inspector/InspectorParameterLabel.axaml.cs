using Avalonia;
using Avalonia.Controls;

namespace Prokudin.Gui.Views.Inspector;

public sealed partial class InspectorParameterLabel : UserControl
{
    public static readonly StyledProperty<string> CaptionProperty =
        AvaloniaProperty.Register<InspectorParameterLabel, string>(nameof(Caption));

    public static readonly StyledProperty<string> ShortTipProperty =
        AvaloniaProperty.Register<InspectorParameterLabel, string>(nameof(ShortTip));

    public static readonly StyledProperty<string> LongTipProperty =
        AvaloniaProperty.Register<InspectorParameterLabel, string>(nameof(LongTip));

    public InspectorParameterLabel()
    {
        InitializeComponent();
    }

    public string Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
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
}
