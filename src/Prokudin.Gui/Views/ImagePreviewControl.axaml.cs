using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Views;

public sealed partial class ImagePreviewControl : UserControl
{
    public static readonly StyledProperty<Bitmap?> DisplayBitmapProperty =
        AvaloniaProperty.Register<ImagePreviewControl, Bitmap?>(nameof(DisplayBitmap));

    public static readonly StyledProperty<PreviewZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<ImagePreviewControl, PreviewZoomMode>(nameof(ZoomMode), PreviewZoomMode.OneToOne);

    public static readonly StyledProperty<ImageSelectionRect> SelectionRectProperty =
        AvaloniaProperty.Register<ImagePreviewControl, ImageSelectionRect>(
            nameof(SelectionRect),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> HasImageProperty =
        AvaloniaProperty.Register<ImagePreviewControl, bool>(nameof(HasImage));

    private Point? selectionStart;
    private bool isSelecting;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DisplayBitmapProperty ||
            change.Property == ZoomModeProperty ||
            change.Property == SelectionRectProperty)
        {
            ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        PreviewImage.Source = DisplayBitmap;

        if (DisplayBitmap is null)
        {
            ImageHost.Width = double.NaN;
            ImageHost.Height = double.NaN;
            ImageHost.MinWidth = 0;
            ImageHost.MinHeight = 0;
            SelectionRectangle.IsVisible = false;
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
            ImageHost.Width = pixelSize.Width;
            ImageHost.Height = pixelSize.Height;
            ImageHost.MinWidth = pixelSize.Width;
            ImageHost.MinHeight = pixelSize.Height;
            PreviewImage.Width = pixelSize.Width;
            PreviewImage.Height = pixelSize.Height;
            SelectionOverlay.Width = pixelSize.Width;
            SelectionOverlay.Height = pixelSize.Height;
            UpdateSelectionOverlay(1.0, 0, 0);
        }
        else
        {
            ScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PreviewImage.Stretch = Stretch.Uniform;
            PreviewImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            PreviewImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ImageHost.Width = double.NaN;
            ImageHost.Height = double.NaN;
            ImageHost.MinWidth = 0;
            ImageHost.MinHeight = 0;
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
            ImageHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            ImageHost.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

            var viewport = ScrollViewer.Viewport;
            ImageHost.Width = viewport.Width;
            ImageHost.Height = viewport.Height;
            SelectionOverlay.Width = viewport.Width;
            SelectionOverlay.Height = viewport.Height;

            var (scale, offsetX, offsetY) = GetFitTransform(pixelSize.Width, pixelSize.Height, viewport.Width, viewport.Height);
            UpdateSelectionOverlay(scale, offsetX, offsetY);
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

        isSelecting = true;
        selectionStart = imagePoint;
        SelectionRect = ImageSelectionRect.FromPoints(imagePoint.X, imagePoint.Y, imagePoint.X, imagePoint.Y);
        e.Pointer.Capture(ImageHost);
        e.Handled = true;
    }

    private void ImageHost_OnPointerMoved(object? sender, PointerEventArgs e)
    {
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

    private void ImageHost_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!isSelecting)
        {
            return;
        }

        isSelecting = false;
        selectionStart = null;
        e.Pointer.Capture(null);
        e.Handled = true;
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
