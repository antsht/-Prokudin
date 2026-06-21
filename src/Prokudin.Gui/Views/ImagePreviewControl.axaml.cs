using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Windows.Input;
using Prokudin.Core.Retouch;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class ImagePreviewControl : UserControl
{
    public static readonly StyledProperty<Bitmap?> DisplayBitmapProperty =
        AvaloniaProperty.Register<ImagePreviewControl, Bitmap?>(nameof(DisplayBitmap));

    public static readonly StyledProperty<Bitmap?> MaskOverlayBitmapProperty =
        AvaloniaProperty.Register<ImagePreviewControl, Bitmap?>(nameof(MaskOverlayBitmap));

    public static readonly StyledProperty<PreviewZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<ImagePreviewControl, PreviewZoomMode>(nameof(ZoomMode), PreviewZoomMode.OneToOne);

    public static readonly StyledProperty<ImageSelectionRect> SelectionRectProperty =
        AvaloniaProperty.Register<ImagePreviewControl, ImageSelectionRect>(
            nameof(SelectionRect),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> HasImageProperty =
        AvaloniaProperty.Register<ImagePreviewControl, bool>(nameof(HasImage));

    public static readonly StyledProperty<PreviewInteractionMode> InteractionModeProperty =
        AvaloniaProperty.Register<ImagePreviewControl, PreviewInteractionMode>(
            nameof(InteractionMode),
            PreviewInteractionMode.Selection);

    public static readonly StyledProperty<RetouchTool> RetouchToolProperty =
        AvaloniaProperty.Register<ImagePreviewControl, RetouchTool>(nameof(RetouchTool), RetouchTool.Heal);

    public static readonly StyledProperty<string?> PreviewImageContextKeyProperty =
        AvaloniaProperty.Register<ImagePreviewControl, string?>(nameof(PreviewImageContextKey));

    public static readonly StyledProperty<int> BrushSizeProperty =
        AvaloniaProperty.Register<ImagePreviewControl, int>(nameof(BrushSize), 12);

    public static readonly StyledProperty<int> InpaintRadiusProperty =
        AvaloniaProperty.Register<ImagePreviewControl, int>(nameof(InpaintRadius), 3);

    public static readonly StyledProperty<ICommand?> RetouchStrokeCommandProperty =
        AvaloniaProperty.Register<ImagePreviewControl, ICommand?>(nameof(RetouchStrokeCommand));

    public static readonly StyledProperty<ICommand?> StampStrokeCommandProperty =
        AvaloniaProperty.Register<ImagePreviewControl, ICommand?>(nameof(StampStrokeCommand));

    public static readonly StyledProperty<ICommand?> MaskEditCommandProperty =
        AvaloniaProperty.Register<ImagePreviewControl, ICommand?>(nameof(MaskEditCommand));

    private Point? selectionStart;
    private bool isSelecting;
    private List<RetouchPoint>? retouchPoints;
    private List<Point>? retouchPreviewPoints;
    private Point? retouchCursorImagePoint;
    private bool isRetouching;
    private RetouchPoint? stampSourceAnchor;
    private RetouchStroke? stampSourceMaskStroke;
    private List<Point>? stampSourceMaskPreviewPoints;
    private List<RetouchPoint>? stampSourcePoints;
    private List<Point>? stampSourcePreviewPoints;
    private bool isCapturingStampSource;
    private List<RetouchPoint>? stampDestinationPoints;
    private List<Point>? stampDestinationPreviewPoints;
    private Point? stampDestinationAnchorImagePoint;
    private bool isStamping;
    private Point? maskEditStart;
    private Point? maskEditCurrent;
    private AutoCleanMaskEditAction? maskEditAction;
    private bool isEditingMask;

    public ImagePreviewControl()
    {
        InitializeComponent();
        ScrollViewer.SizeChanged += (_, _) => ApplyLayout();
    }

    public Bitmap? DisplayBitmap
    {
        get => GetValue(DisplayBitmapProperty);
        set => SetValue(DisplayBitmapProperty, value);
    }

    public Bitmap? MaskOverlayBitmap
    {
        get => GetValue(MaskOverlayBitmapProperty);
        set => SetValue(MaskOverlayBitmapProperty, value);
    }

    public PreviewZoomMode ZoomMode
    {
        get => GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }

    public ImageSelectionRect SelectionRect
    {
        get => GetValue(SelectionRectProperty);
        set => SetValue(SelectionRectProperty, value);
    }

    public bool HasImage
    {
        get => GetValue(HasImageProperty);
        set => SetValue(HasImageProperty, value);
    }

    public PreviewInteractionMode InteractionMode
    {
        get => GetValue(InteractionModeProperty);
        set => SetValue(InteractionModeProperty, value);
    }

    public RetouchTool RetouchTool
    {
        get => GetValue(RetouchToolProperty);
        set => SetValue(RetouchToolProperty, value);
    }

    public string? PreviewImageContextKey
    {
        get => GetValue(PreviewImageContextKeyProperty);
        set => SetValue(PreviewImageContextKeyProperty, value);
    }

    public int BrushSize
    {
        get => GetValue(BrushSizeProperty);
        set => SetValue(BrushSizeProperty, value);
    }

    public int InpaintRadius
    {
        get => GetValue(InpaintRadiusProperty);
        set => SetValue(InpaintRadiusProperty, value);
    }

    public ICommand? RetouchStrokeCommand
    {
        get => GetValue(RetouchStrokeCommandProperty);
        set => SetValue(RetouchStrokeCommandProperty, value);
    }

    public ICommand? StampStrokeCommand
    {
        get => GetValue(StampStrokeCommandProperty);
        set => SetValue(StampStrokeCommandProperty, value);
    }

    public ICommand? MaskEditCommand
    {
        get => GetValue(MaskEditCommandProperty);
        set => SetValue(MaskEditCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DisplayBitmapProperty ||
            change.Property == MaskOverlayBitmapProperty ||
            change.Property == ZoomModeProperty ||
            change.Property == HasImageProperty ||
            change.Property == SelectionRectProperty)
        {
            ApplyLayout();
        }

        if (change.Property == BrushSizeProperty ||
            change.Property == InpaintRadiusProperty)
        {
            UpdateRetouchOverlay();
        }

        if (change.Property == RetouchToolProperty ||
            change.Property == PreviewImageContextKeyProperty)
        {
            ClearRetouchOverlayState();
            if (InteractionMode == PreviewInteractionMode.Retouch)
            {
                UpdateRetouchOverlay();
            }
        }

        if (change.Property == InteractionModeProperty)
        {
            if (InteractionMode == PreviewInteractionMode.Retouch)
            {
                ClearMaskEditState();
                UpdateRetouchOverlay();
            }
            else
            {
                ClearRetouchOverlayState();
                if (InteractionMode != PreviewInteractionMode.MaskReview)
                {
                    ClearMaskEditState();
                }
            }
        }
    }

    private void ApplyLayout()
    {
        PreviewImage.Source = DisplayBitmap;
        MaskOverlayImage.Source = MaskOverlayBitmap;
        MaskOverlayImage.IsVisible = MaskOverlayBitmap is not null && DisplayBitmap is not null;

        if (DisplayBitmap is null)
        {
            ImageHost.Width = double.NaN;
            ImageHost.Height = double.NaN;
            ImageHost.MinWidth = 0;
            ImageHost.MinHeight = 0;
            SelectionRectangle.IsVisible = false;
            MaskEditRectangle.IsVisible = false;
            MaskOverlayImage.IsVisible = false;
            ClearRetouchOverlayState();
            return;
        }

        var pixelSize = DisplayBitmap.PixelSize;

        if (ZoomMode == PreviewZoomMode.OneToOne)
        {
            ScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            PreviewImage.Stretch = Stretch.None;
            PreviewImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            PreviewImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            MaskOverlayImage.Stretch = Stretch.None;
            MaskOverlayImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            MaskOverlayImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            ImageHost.Width = pixelSize.Width;
            ImageHost.Height = pixelSize.Height;
            ImageHost.MinWidth = pixelSize.Width;
            ImageHost.MinHeight = pixelSize.Height;
            PreviewImage.Width = pixelSize.Width;
            PreviewImage.Height = pixelSize.Height;
            MaskOverlayImage.Width = pixelSize.Width;
            MaskOverlayImage.Height = pixelSize.Height;
            SelectionOverlay.Width = pixelSize.Width;
            SelectionOverlay.Height = pixelSize.Height;
            RetouchOverlay.Width = pixelSize.Width;
            RetouchOverlay.Height = pixelSize.Height;
            UpdateSelectionOverlay(1.0, 0, 0);
            UpdateMaskEditPreview(1.0, 0, 0);
            UpdateRetouchOverlay(1.0, 0, 0);
        }
        else
        {
            ScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PreviewImage.Stretch = Stretch.Uniform;
            PreviewImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            PreviewImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            MaskOverlayImage.Stretch = Stretch.Uniform;
            MaskOverlayImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            MaskOverlayImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ImageHost.Width = double.NaN;
            ImageHost.Height = double.NaN;
            ImageHost.MinWidth = 0;
            ImageHost.MinHeight = 0;
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
            MaskOverlayImage.Width = double.NaN;
            MaskOverlayImage.Height = double.NaN;
            ImageHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            ImageHost.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

            var viewport = ScrollViewer.Viewport;
            ImageHost.Width = viewport.Width;
            ImageHost.Height = viewport.Height;
            SelectionOverlay.Width = viewport.Width;
            SelectionOverlay.Height = viewport.Height;
            RetouchOverlay.Width = viewport.Width;
            RetouchOverlay.Height = viewport.Height;

            var (scale, offsetX, offsetY) = GetFitTransform(pixelSize.Width, pixelSize.Height, viewport.Width, viewport.Height);
            UpdateSelectionOverlay(scale, offsetX, offsetY);
            UpdateMaskEditPreview(scale, offsetX, offsetY);
            UpdateRetouchOverlay(scale, offsetX, offsetY);
        }
    }

    private void UpdateSelectionOverlay(double scale, double offsetX, double offsetY)
    {
        if (SelectionRect.IsEmpty)
        {
            SelectionRectangle.IsVisible = false;
            return;
        }

        SelectionRectangle.IsVisible = true;
        Canvas.SetLeft(SelectionRectangle, offsetX + (SelectionRect.X * scale));
        Canvas.SetTop(SelectionRectangle, offsetY + (SelectionRect.Y * scale));
        SelectionRectangle.Width = SelectionRect.Width * scale;
        SelectionRectangle.Height = SelectionRect.Height * scale;
    }

    private void UpdateMaskEditPreview(double scale, double offsetX, double offsetY)
    {
        if (!isEditingMask ||
            maskEditStart is not { } start ||
            maskEditCurrent is not { } current ||
            DisplayBitmap is null)
        {
            MaskEditRectangle.IsVisible = false;
            return;
        }

        var rect = ImageSelectionRect.FromPoints(start.X, start.Y, current.X, current.Y)
            .Clamp(DisplayBitmap.PixelSize.Width, DisplayBitmap.PixelSize.Height);
        if (rect.IsEmpty)
        {
            MaskEditRectangle.IsVisible = false;
            return;
        }

        MaskEditRectangle.IsVisible = true;
        Canvas.SetLeft(MaskEditRectangle, offsetX + (rect.X * scale));
        Canvas.SetTop(MaskEditRectangle, offsetY + (rect.Y * scale));
        MaskEditRectangle.Width = rect.Width * scale;
        MaskEditRectangle.Height = rect.Height * scale;
    }

    private void UpdateMaskEditPreview()
    {
        var (scale, offsetX, offsetY) = GetCurrentImageTransform();
        UpdateMaskEditPreview(scale, offsetX, offsetY);
    }

    private static (double Scale, double OffsetX, double OffsetY) GetFitTransform(
        int imageWidth,
        int imageHeight,
        double hostWidth,
        double hostHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || hostWidth <= 0 || hostHeight <= 0)
        {
            return (1.0, 0, 0);
        }

        var scale = Math.Min(hostWidth / imageWidth, hostHeight / imageHeight);
        var renderedWidth = imageWidth * scale;
        var renderedHeight = imageHeight * scale;
        var offsetX = (hostWidth - renderedWidth) / 2.0;
        var offsetY = (hostHeight - renderedHeight) / 2.0;
        return (scale, offsetX, offsetY);
    }

    private (double Scale, double OffsetX, double OffsetY) GetCurrentImageTransform()
    {
        if (DisplayBitmap is not { } bitmap)
        {
            return (1.0, 0, 0);
        }

        if (ZoomMode == PreviewZoomMode.OneToOne)
        {
            return (1.0, 0, 0);
        }

        var pixelSize = bitmap.PixelSize;
        return GetFitTransform(pixelSize.Width, pixelSize.Height, ImageHost.Bounds.Width, ImageHost.Bounds.Height);
    }

    private void UpdateRetouchOverlay()
    {
        var (scale, offsetX, offsetY) = GetCurrentImageTransform();
        UpdateRetouchOverlay(scale, offsetX, offsetY);
    }

    private void UpdateRetouchOverlay(double scale, double offsetX, double offsetY)
    {
        if (DisplayBitmap is null || !HasImage || InteractionMode != PreviewInteractionMode.Retouch)
        {
            HideRetouchOverlay();
            return;
        }

        UpdateRetouchCursor(scale, offsetX, offsetY);
        UpdateRetouchStrokePreview(scale, offsetX, offsetY);
        UpdateStampSourcePreview(scale, offsetX, offsetY);
        UpdateStampGhost(scale, offsetX, offsetY);
    }

    private void UpdateRetouchCursor(double scale, double offsetX, double offsetY)
    {
        if (retouchCursorImagePoint is not { } imagePoint)
        {
            RetouchBrushCircle.IsVisible = false;
            RetouchRadiusCircle.IsVisible = false;
            return;
        }

        var geometry = RetouchPreviewGeometryCalculator.CalculateCursor(
            HasImage,
            InteractionMode,
            imagePoint.X,
            imagePoint.Y,
            BrushSize,
            InpaintRadius,
            scale,
            offsetX,
            offsetY);

        if (!geometry.IsVisible)
        {
            RetouchBrushCircle.IsVisible = false;
            RetouchRadiusCircle.IsVisible = false;
            return;
        }

        PositionCircle(RetouchBrushCircle, geometry.CenterX, geometry.CenterY, geometry.BrushDiameter);
        PositionCircle(RetouchRadiusCircle, geometry.CenterX, geometry.CenterY, geometry.OuterDiameter);
    }

    private void UpdateRetouchStrokePreview(double scale, double offsetX, double offsetY)
    {
        var points = RetouchTool == RetouchTool.Stamp
            ? stampDestinationPreviewPoints
            : retouchPreviewPoints;

        if (points is not { Count: > 1 })
        {
            RetouchStrokePreview.IsVisible = false;
            RetouchStrokePreview.Data = null;
            return;
        }

        RetouchStrokePreview.Data = BuildStrokeGeometry(points, scale, offsetX, offsetY);
        RetouchStrokePreview.StrokeThickness = Math.Clamp(BrushSize, 1, 200) * scale;
        RetouchStrokePreview.IsVisible = true;
    }

    private void UpdateStampSourcePreview(double scale, double offsetX, double offsetY)
    {
        if (RetouchTool != RetouchTool.Stamp)
        {
            StampSourcePreview.IsVisible = false;
            StampSourcePreview.Data = null;
            return;
        }

        var points = isCapturingStampSource
            ? stampSourcePreviewPoints
            : TranslatedStampSourceMaskPreview();

        if (points is not { Count: > 1 })
        {
            StampSourcePreview.IsVisible = false;
            StampSourcePreview.Data = null;
            return;
        }

        StampSourcePreview.Data = BuildStrokeGeometry(points, scale, offsetX, offsetY);
        StampSourcePreview.StrokeThickness = Math.Max(1.0, Math.Clamp(BrushSize, 1, 200) * scale);
        StampSourcePreview.IsVisible = true;
    }

    private void UpdateStampGhost(double scale, double offsetX, double offsetY)
    {
        if (RetouchTool != RetouchTool.Stamp ||
            DisplayBitmap is not { } bitmap ||
            stampSourceAnchor is null ||
            retouchCursorImagePoint is not { } destinationCenter ||
            TryGetStampPreviewSourceCenter() is not { } sourceCenter)
        {
            StampGhostClip.IsVisible = false;
            StampGhostImage.Source = null;
            return;
        }

        var geometry = RetouchPreviewGeometryCalculator.CalculateStampGhost(
            HasImage,
            InteractionMode,
            RetouchTool,
            sourceCenter.X,
            sourceCenter.Y,
            destinationCenter.X,
            destinationCenter.Y,
            BrushSize,
            scale,
            offsetX,
            offsetY);

        if (!geometry.IsVisible)
        {
            StampGhostClip.IsVisible = false;
            StampGhostImage.Source = null;
            return;
        }

        var pixelSize = bitmap.PixelSize;
        StampGhostImage.Source = bitmap;
        StampGhostImage.Width = pixelSize.Width * scale;
        StampGhostImage.Height = pixelSize.Height * scale;
        Canvas.SetLeft(StampGhostImage, offsetX - geometry.SourceLeft);
        Canvas.SetTop(StampGhostImage, offsetY - geometry.SourceTop);

        StampGhostClip.Width = geometry.Diameter;
        StampGhostClip.Height = geometry.Diameter;
        Canvas.SetLeft(StampGhostClip, geometry.DestinationLeft);
        Canvas.SetTop(StampGhostClip, geometry.DestinationTop);
        StampGhostClip.IsVisible = true;
    }

    private void HideRetouchOverlay()
    {
        RetouchBrushCircle.IsVisible = false;
        RetouchRadiusCircle.IsVisible = false;
        RetouchStrokePreview.IsVisible = false;
        RetouchStrokePreview.Data = null;
        StampSourcePreview.IsVisible = false;
        StampSourcePreview.Data = null;
        StampGhostClip.IsVisible = false;
        StampGhostImage.Source = null;
    }

    private void ClearRetouchOverlayState()
    {
        retouchCursorImagePoint = null;
        retouchPreviewPoints = null;
        retouchPoints = null;
        isRetouching = false;
        stampSourceAnchor = null;
        stampSourceMaskStroke = null;
        stampSourceMaskPreviewPoints = null;
        stampSourcePoints = null;
        stampSourcePreviewPoints = null;
        isCapturingStampSource = false;
        stampDestinationPoints = null;
        stampDestinationPreviewPoints = null;
        stampDestinationAnchorImagePoint = null;
        isStamping = false;
        HideRetouchOverlay();
    }

    private void ClearMaskEditState()
    {
        maskEditStart = null;
        maskEditCurrent = null;
        maskEditAction = null;
        isEditingMask = false;
        MaskEditRectangle.IsVisible = false;
    }

    private bool TryUpdateRetouchCursor(Point hostPoint, bool clampToImage)
    {
        if (TryMapToImage(hostPoint, out var imagePoint))
        {
            retouchCursorImagePoint = imagePoint;
            UpdateRetouchOverlay();
            return true;
        }

        if (clampToImage && DisplayBitmap is not null)
        {
            retouchCursorImagePoint = ClampPointToImage(hostPoint);
            UpdateRetouchOverlay();
            return true;
        }

        retouchCursorImagePoint = null;
        UpdateRetouchOverlay();
        return false;
    }

    private static void PositionCircle(Ellipse circle, double centerX, double centerY, double diameter)
    {
        circle.Width = diameter;
        circle.Height = diameter;
        Canvas.SetLeft(circle, centerX - (diameter / 2.0));
        Canvas.SetTop(circle, centerY - (diameter / 2.0));
        circle.IsVisible = diameter > 0;
    }

    private static StreamGeometry BuildStrokeGeometry(IReadOnlyList<Point> imagePoints, double scale, double offsetX, double offsetY)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(ToOverlayPoint(imagePoints[0], scale, offsetX, offsetY), isFilled: false);

        for (var i = 1; i < imagePoints.Count; i++)
        {
            context.LineTo(ToOverlayPoint(imagePoints[i], scale, offsetX, offsetY));
        }

        return geometry;
    }

    private static Point ToOverlayPoint(Point imagePoint, double scale, double offsetX, double offsetY)
    {
        return new Point(
            offsetX + (imagePoint.X * scale),
            offsetY + (imagePoint.Y * scale));
    }

    private List<Point>? TranslatedStampSourceMaskPreview()
    {
        if (stampSourceMaskPreviewPoints is not { Count: > 1 } sourcePoints ||
            retouchCursorImagePoint is not { } destinationCenter ||
            TryGetStampPreviewSourceCenter() is not { } sourceCenter)
        {
            return null;
        }

        var translated = new List<Point>(sourcePoints.Count);
        foreach (var point in sourcePoints)
        {
            translated.Add(new Point(
                destinationCenter.X + point.X - sourceCenter.X,
                destinationCenter.Y + point.Y - sourceCenter.Y));
        }

        return translated;
    }

    private Point? TryGetStampPreviewSourceCenter()
    {
        if (stampSourceAnchor is not { } sourceAnchor)
        {
            return null;
        }

        var sourceCenter = ToImagePoint(sourceAnchor);
        if (isStamping &&
            stampDestinationAnchorImagePoint is { } destinationAnchor &&
            retouchCursorImagePoint is { } cursor)
        {
            return new Point(
                sourceCenter.X + cursor.X - destinationAnchor.X,
                sourceCenter.Y + cursor.Y - destinationAnchor.Y);
        }

        return sourceCenter;
    }

    private static RetouchPoint ToRetouchPoint(Point point)
    {
        return new RetouchPoint((float)point.X, (float)point.Y);
    }

    private static Point ToImagePoint(RetouchPoint point)
    {
        return new Point(point.X, point.Y);
    }

    private bool TryMapToImage(Point point, out Point imagePoint)
    {
        imagePoint = default;

        if (DisplayBitmap is null || !HasImage)
        {
            return false;
        }

        var pixelSize = DisplayBitmap.PixelSize;

        if (ZoomMode == PreviewZoomMode.OneToOne)
        {
            if (point.X < 0 || point.Y < 0 || point.X > pixelSize.Width || point.Y > pixelSize.Height)
            {
                return false;
            }

            imagePoint = point;
            return true;
        }

        var hostWidth = ImageHost.Bounds.Width;
        var hostHeight = ImageHost.Bounds.Height;
        var (scale, offsetX, offsetY) = GetFitTransform(pixelSize.Width, pixelSize.Height, hostWidth, hostHeight);
        var renderedWidth = pixelSize.Width * scale;
        var renderedHeight = pixelSize.Height * scale;

        if (point.X < offsetX || point.Y < offsetY ||
            point.X > offsetX + renderedWidth || point.Y > offsetY + renderedHeight)
        {
            return false;
        }

        imagePoint = new Point((point.X - offsetX) / scale, (point.Y - offsetY) / scale);
        return true;
    }

    private void ImageHost_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!HasImage || DisplayBitmap is null)
        {
            return;
        }

        var point = e.GetPosition(ImageHost);
        if (!TryMapToImage(point, out var imagePoint))
        {
            return;
        }

        if (InteractionMode == PreviewInteractionMode.MaskReview)
        {
            BeginMaskEditInteraction(e, imagePoint);
            return;
        }

        if (InteractionMode == PreviewInteractionMode.Retouch)
        {
            if (RetouchTool == RetouchTool.Stamp)
            {
                BeginStampInteraction(e, imagePoint);
                return;
            }

            isRetouching = true;
            retouchPoints = [ToRetouchPoint(imagePoint)];
            retouchPreviewPoints = [imagePoint];
            retouchCursorImagePoint = imagePoint;
            UpdateRetouchOverlay();
            e.Pointer.Capture(ImageHost);
            e.Handled = true;
            return;
        }

        isSelecting = true;
        selectionStart = imagePoint;
        SelectionRect = ImageSelectionRect.FromPoints(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y);
        e.Pointer.Capture(ImageHost);
        e.Handled = true;
    }

    private void BeginMaskEditInteraction(PointerPressedEventArgs e, Point imagePoint)
    {
        if (!TryGetMaskEditAction(e.KeyModifiers, out var action))
        {
            e.Handled = true;
            return;
        }

        isEditingMask = true;
        maskEditStart = imagePoint;
        maskEditCurrent = imagePoint;
        maskEditAction = action;
        UpdateMaskEditPreview();
        e.Pointer.Capture(ImageHost);
        e.Handled = true;
    }

    private void BeginStampInteraction(PointerPressedEventArgs e, Point imagePoint)
    {
        retouchCursorImagePoint = imagePoint;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            isCapturingStampSource = true;
            stampSourceAnchor = ToRetouchPoint(imagePoint);
            stampSourcePoints = [ToRetouchPoint(imagePoint)];
            stampSourcePreviewPoints = [imagePoint];
            stampSourceMaskStroke = null;
            stampSourceMaskPreviewPoints = null;
            UpdateRetouchOverlay();
            e.Pointer.Capture(ImageHost);
            e.Handled = true;
            return;
        }

        if (stampSourceAnchor is null)
        {
            UpdateRetouchOverlay();
            e.Handled = true;
            return;
        }

        isStamping = true;
        stampDestinationAnchorImagePoint = imagePoint;
        stampDestinationPoints = [ToRetouchPoint(imagePoint)];
        stampDestinationPreviewPoints = [imagePoint];
        UpdateRetouchOverlay();
        e.Pointer.Capture(ImageHost);
        e.Handled = true;
    }

    private void ImageHost_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (InteractionMode == PreviewInteractionMode.MaskReview)
        {
            UpdateMaskEditInteraction(e);
            return;
        }

        if (InteractionMode == PreviewInteractionMode.Retouch)
        {
            var retouchPoint = e.GetPosition(ImageHost);
            if (!TryUpdateRetouchCursor(retouchPoint, isRetouching || isCapturingStampSource || isStamping))
            {
                return;
            }

            if (RetouchTool == RetouchTool.Stamp)
            {
                UpdateStampInteraction(e);
                return;
            }

            if (isRetouching && retouchPoints is not null && retouchCursorImagePoint is { } retouchImagePoint)
            {
                retouchPoints.Add(ToRetouchPoint(retouchImagePoint));
                retouchPreviewPoints ??= [];
                retouchPreviewPoints.Add(retouchImagePoint);
                UpdateRetouchOverlay();
                e.Handled = true;
            }

            return;
        }

        if (!isSelecting || selectionStart is not { } start)
        {
            return;
        }

        var point = e.GetPosition(ImageHost);
        if (!TryMapToImage(point, out var imagePoint))
        {
            imagePoint = ClampPointToImage(point);
        }

        SelectionRect = ImageSelectionRect.FromPoints(start.X, start.Y, imagePoint.X, imagePoint.Y)
            .Clamp(DisplayBitmap!.PixelSize.Width, DisplayBitmap.PixelSize.Height);
        e.Handled = true;
    }

    private void UpdateStampInteraction(PointerEventArgs e)
    {
        if (isCapturingStampSource && stampSourcePoints is not null && retouchCursorImagePoint is { } sourcePoint)
        {
            stampSourcePoints.Add(ToRetouchPoint(sourcePoint));
            stampSourcePreviewPoints ??= [];
            stampSourcePreviewPoints.Add(sourcePoint);
            UpdateRetouchOverlay();
            e.Handled = true;
            return;
        }

        if (isStamping && stampDestinationPoints is not null && retouchCursorImagePoint is { } destinationPoint)
        {
            stampDestinationPoints.Add(ToRetouchPoint(destinationPoint));
            stampDestinationPreviewPoints ??= [];
            stampDestinationPreviewPoints.Add(destinationPoint);
            UpdateRetouchOverlay();
            e.Handled = true;
        }
    }

    private void UpdateMaskEditInteraction(PointerEventArgs e)
    {
        if (!isEditingMask)
        {
            return;
        }

        var point = e.GetPosition(ImageHost);
        if (!TryMapToImage(point, out var imagePoint))
        {
            imagePoint = ClampPointToImage(point);
        }

        maskEditCurrent = imagePoint;
        UpdateMaskEditPreview();
        e.Handled = true;
    }

    private void ImageHost_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (isEditingMask)
        {
            FinishMaskEditInteraction(e);
            return;
        }

        if (isCapturingStampSource)
        {
            isCapturingStampSource = false;
            var points = stampSourcePoints?.ToArray() ?? [];
            var previewPoints = stampSourcePreviewPoints?.ToArray() ?? [];
            stampSourcePoints = null;
            stampSourcePreviewPoints = null;
            e.Pointer.Capture(null);

            if (points.Length > 0)
            {
                stampSourceAnchor = points[0];
                if (points.Length > 1)
                {
                    stampSourceMaskStroke = new RetouchStroke(points, Math.Clamp(BrushSize, 1, 200));
                    stampSourceMaskPreviewPoints = previewPoints.ToList();
                }
                else
                {
                    stampSourceMaskStroke = null;
                    stampSourceMaskPreviewPoints = null;
                }
            }

            UpdateRetouchOverlay();
            e.Handled = true;
            return;
        }

        if (isStamping)
        {
            isStamping = false;
            var points = stampDestinationPoints?.ToArray() ?? [];
            stampDestinationPoints = null;
            stampDestinationPreviewPoints = null;
            stampDestinationAnchorImagePoint = null;
            e.Pointer.Capture(null);

            if (stampSourceAnchor is { } sourceAnchor)
            {
                var stroke = new CloneStampStroke(
                    sourceAnchor,
                    new RetouchStroke(points, Math.Clamp(BrushSize, 1, 200)),
                    Math.Clamp(InpaintRadius, 1, 24),
                    stampSourceMaskStroke);

                if (StampStrokeCommand?.CanExecute(stroke) == true)
                {
                    StampStrokeCommand.Execute(stroke);
                }
            }

            UpdateRetouchOverlay();
            e.Handled = true;
            return;
        }

        if (isRetouching)
        {
            isRetouching = false;
            var points = retouchPoints?.ToArray() ?? [];
            retouchPoints = null;
            retouchPreviewPoints = null;
            e.Pointer.Capture(null);

            var stroke = new RetouchStroke(points, Math.Clamp(BrushSize, 1, 200));
            if (RetouchStrokeCommand?.CanExecute(stroke) == true)
            {
                RetouchStrokeCommand.Execute(stroke);
            }

            UpdateRetouchOverlay();
            e.Handled = true;
            return;
        }

        if (!isSelecting)
        {
            return;
        }

        isSelecting = false;
        selectionStart = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void FinishMaskEditInteraction(PointerReleasedEventArgs e)
    {
        var start = maskEditStart;
        var current = maskEditCurrent;
        var action = maskEditAction;
        ClearMaskEditState();
        e.Pointer.Capture(null);

        if (start is { } startPoint &&
            current is { } currentPoint &&
            action is { } editAction)
        {
            var isRectangle = Math.Abs(startPoint.X - currentPoint.X) >= 2.0 ||
                              Math.Abs(startPoint.Y - currentPoint.Y) >= 2.0;
            var operation = new AutoCleanMaskEditOperation(
                ToRetouchPoint(startPoint),
                ToRetouchPoint(currentPoint),
                editAction,
                Math.Clamp(BrushSize, 1, 200),
                isRectangle);

            if (MaskEditCommand?.CanExecute(operation) == true)
            {
                MaskEditCommand.Execute(operation);
            }
        }

        e.Handled = true;
    }

    private static bool TryGetMaskEditAction(KeyModifiers modifiers, out AutoCleanMaskEditAction action)
    {
        var add = modifiers.HasFlag(KeyModifiers.Control);
        var remove = modifiers.HasFlag(KeyModifiers.Alt);
        if (add == remove)
        {
            action = default;
            return false;
        }

        action = add ? AutoCleanMaskEditAction.Add : AutoCleanMaskEditAction.Remove;
        return true;
    }

    private void ImageHost_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (isRetouching || isCapturingStampSource || isStamping || isEditingMask)
        {
            return;
        }

        retouchCursorImagePoint = null;
        UpdateRetouchOverlay();
    }

    private Point ClampPointToImage(Point point)
    {
        if (DisplayBitmap is null)
        {
            return point;
        }

        var pixelSize = DisplayBitmap.PixelSize;

        if (ZoomMode == PreviewZoomMode.OneToOne)
        {
            return new Point(
                Math.Clamp(point.X, 0, pixelSize.Width),
                Math.Clamp(point.Y, 0, pixelSize.Height));
        }

        var hostWidth = ImageHost.Bounds.Width;
        var hostHeight = ImageHost.Bounds.Height;
        var (scale, offsetX, offsetY) = GetFitTransform(pixelSize.Width, pixelSize.Height, hostWidth, hostHeight);
        var renderedWidth = pixelSize.Width * scale;
        var renderedHeight = pixelSize.Height * scale;
        var clamped = new Point(
            Math.Clamp(point.X, offsetX, offsetX + renderedWidth),
            Math.Clamp(point.Y, offsetY, offsetY + renderedHeight));
        return new Point(
            (clamped.X - offsetX) / scale,
            (clamped.Y - offsetY) / scale);
    }
}
