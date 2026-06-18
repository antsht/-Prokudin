using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Imaging;

namespace Prokudin.Gui.ViewModels;

public sealed partial class ChannelSlotViewModel : ObservableObject, IDisposable
{
    public ChannelSlotViewModel(string displayName, ChannelName? channelName)
    {
        DisplayName = displayName;
        ChannelName = channelName;
    }

    public string DisplayName { get; }

    public ChannelName? ChannelName { get; }

    public bool CanSwap => ChannelName.HasValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(Dimensions))]
    [NotifyPropertyChangedFor(nameof(DisplayImage))]
    private ImageBuffer? image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(Dimensions))]
    [NotifyPropertyChangedFor(nameof(DisplayImage))]
    private RgbImageBuffer? result;

    [ObservableProperty]
    private string? sourcePath;

    [ObservableProperty]
    private Bitmap? displayBitmap;

    public bool HasImage => Image is not null || Result is not null;

    public string Dimensions
    {
        get
        {
            if (Image is not null)
            {
                return $"{Image.Width} x {Image.Height}";
            }

            if (Result is not null)
            {
                return $"{Result.Width} x {Result.Height}";
            }

            return "empty";
        }
    }

    public object? DisplayImage => Result is not null ? Result : Image;

    public void Dispose()
    {
        DisplayBitmap?.Dispose();
        DisplayBitmap = null;
    }

    partial void OnImageChanged(ImageBuffer? value)
    {
        RefreshDisplayBitmap();
    }

    partial void OnResultChanged(RgbImageBuffer? value)
    {
        RefreshDisplayBitmap();
    }

    private void RefreshDisplayBitmap()
    {
        var previous = DisplayBitmap;
        DisplayBitmap = Result is not null
            ? AvaloniaBitmapFactory.FromRgbImageBuffer(Result)
            : Image is not null
                ? AvaloniaBitmapFactory.FromImageBuffer(Image)
                : null;
        previous?.Dispose();
    }
}
