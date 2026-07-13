using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Prokudin.Gui.Views;

/// <summary>
/// Draws a normalized levels histogram and exposes draggable black, gamma, and white handles.
/// Exact values remain available through the paired numeric controls.
/// </summary>
public sealed class LevelsHistogram : Control
{
    private const double HorizontalPadding = 10;
    private const double VerticalPadding = 8;
    private const double MinimumGap = 0.01;
    private const double HandleHitRadius = 12;
    private DragHandle activeHandle;

    public static readonly StyledProperty<IReadOnlyList<double>?> BinsProperty =
        AvaloniaProperty.Register<LevelsHistogram, IReadOnlyList<double>?>(nameof(Bins));

    public static readonly StyledProperty<double> BlackPointProperty =
        AvaloniaProperty.Register<LevelsHistogram, double>(
            nameof(BlackPoint),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> WhitePointProperty =
        AvaloniaProperty.Register<LevelsHistogram, double>(
            nameof(WhitePoint),
            1.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> GammaProperty =
        AvaloniaProperty.Register<LevelsHistogram, double>(
            nameof(Gamma),
            1.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> CanEditProperty =
        AvaloniaProperty.Register<LevelsHistogram, bool>(nameof(CanEdit));

    public static readonly StyledProperty<IBrush?> HistogramBrushProperty =
        AvaloniaProperty.Register<LevelsHistogram, IBrush?>(nameof(HistogramBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<LevelsHistogram, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> HandleBrushProperty =
        AvaloniaProperty.Register<LevelsHistogram, IBrush?>(nameof(HandleBrush));

    static LevelsHistogram()
    {
        AffectsRender<LevelsHistogram>(
            BinsProperty,
            BlackPointProperty,
            WhitePointProperty,
            GammaProperty,
            CanEditProperty,
            HistogramBrushProperty,
            GridBrushProperty,
            HandleBrushProperty);
    }

    public IReadOnlyList<double>? Bins
    {
        get => GetValue(BinsProperty);
        set => SetValue(BinsProperty, value);
    }

    public double BlackPoint
    {
        get => GetValue(BlackPointProperty);
        set => SetValue(BlackPointProperty, value);
    }

    public double WhitePoint
    {
        get => GetValue(WhitePointProperty);
        set => SetValue(WhitePointProperty, value);
    }

    public double Gamma
    {
        get => GetValue(GammaProperty);
        set => SetValue(GammaProperty, value);
    }

    public bool CanEdit
    {
        get => GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    public IBrush? HistogramBrush
    {
        get => GetValue(HistogramBrushProperty);
        set => SetValue(HistogramBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? HandleBrush
    {
        get => GetValue(HandleBrushProperty);
        set => SetValue(HandleBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var plot = GetPlotBounds();
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        var gridBrush = GridBrush ?? Brushes.Gray;
        var histogramBrush = HistogramBrush ?? Brushes.DodgerBlue;
        var handleBrush = HandleBrush ?? Brushes.DodgerBlue;

        context.DrawLine(new Pen(gridBrush, 1), new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));
        context.DrawLine(new Pen(gridBrush, 1), new Point(plot.X, plot.Y + (plot.Height * 0.5)), new Point(plot.Right, plot.Y + (plot.Height * 0.5)));

        if (Bins is { Count: > 0 } bins)
        {
            var barWidth = plot.Width / bins.Count;
            for (var index = 0; index < bins.Count; index++)
            {
                var height = Math.Clamp(bins[index], 0.0, 1.0) * plot.Height;
                var width = Math.Max(1, barWidth - 1);
                context.DrawRectangle(
                    histogramBrush,
                    null,
                    new Rect(plot.X + (index * barWidth), plot.Bottom - height, width, height));
            }
        }

        DrawHandle(context, handleBrush, plot, Clamp(BlackPoint));
        DrawHandle(context, handleBrush, plot, GammaToPosition(Gamma));
        DrawHandle(context, handleBrush, plot, Clamp(WhitePoint));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!CanEdit || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var plot = GetPlotBounds();
        if (plot.Width <= 0)
        {
            return;
        }

        var position = e.GetPosition(this).X;
        activeHandle = FindClosestHandle(position, plot);
        if (activeHandle == DragHandle.None)
        {
            return;
        }

        e.Pointer.Capture(this);
        UpdateActiveHandle(position, plot);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (activeHandle == DragHandle.None || !ReferenceEquals(e.Pointer.Captured, this))
        {
            return;
        }

        UpdateActiveHandle(e.GetPosition(this).X, GetPlotBounds());
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (activeHandle == DragHandle.None)
        {
            return;
        }

        e.Pointer.Capture(null);
        activeHandle = DragHandle.None;
        e.Handled = true;
    }

    private Rect GetPlotBounds() => new(
        HorizontalPadding,
        VerticalPadding,
        Math.Max(0, Bounds.Width - (HorizontalPadding * 2)),
        Math.Max(0, Bounds.Height - (VerticalPadding * 2)));

    private DragHandle FindClosestHandle(double position, Rect plot)
    {
        var black = PositionToX(Clamp(BlackPoint), plot);
        var gamma = PositionToX(GammaToPosition(Gamma), plot);
        var white = PositionToX(Clamp(WhitePoint), plot);
        var candidates = new[]
        {
            (DragHandle.Black, Math.Abs(position - black)),
            (DragHandle.Gamma, Math.Abs(position - gamma)),
            (DragHandle.White, Math.Abs(position - white)),
        };
        var nearest = candidates.MinBy(candidate => candidate.Item2);
        return nearest.Item2 <= HandleHitRadius ? nearest.Item1 : DragHandle.None;
    }

    private void UpdateActiveHandle(double x, Rect plot)
    {
        if (plot.Width <= 0)
        {
            return;
        }

        var position = Clamp((x - plot.X) / plot.Width);
        switch (activeHandle)
        {
            case DragHandle.Black:
                SetCurrentValue(BlackPointProperty, Math.Min(position, WhitePoint - MinimumGap));
                break;
            case DragHandle.Gamma:
                SetCurrentValue(GammaProperty, PositionToGamma(position));
                break;
            case DragHandle.White:
                SetCurrentValue(WhitePointProperty, Math.Max(position, BlackPoint + MinimumGap));
                break;
        }
    }

    private static void DrawHandle(DrawingContext context, IBrush brush, Rect plot, double position)
    {
        var x = PositionToX(position, plot);
        var point = new Point(x, plot.Bottom);
        context.DrawLine(new Pen(brush, 1.5), new Point(x, plot.Y), point);
        context.DrawEllipse(brush, null, point, 3.5, 3.5);
    }

    private static double PositionToX(double position, Rect plot) => plot.X + (Clamp(position) * plot.Width);

    private static double GammaToPosition(double gamma) => Math.Pow(0.5, 1.0 / Math.Max(0.1, gamma));

    private static double PositionToGamma(double position) =>
        Math.Clamp(Math.Log(0.5) / Math.Log(Math.Clamp(position, 0.01, 0.99)), 0.1, 5.0);

    private static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);

    private enum DragHandle
    {
        None,
        Black,
        Gamma,
        White,
    }
}
