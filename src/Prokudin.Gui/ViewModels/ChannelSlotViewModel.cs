using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Imaging;
using System.Windows.Input;

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

    public bool CanLoadImage => ChannelName.HasValue;

    public bool IsResultSlot => ChannelName is null;

    public ICommand? OpenCommand { get; set; }

    public ICommand? ExportCommand { get; set; }

    public ICommand? ToggleExportSettingsCommand { get; set; }

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

    [ObservableProperty]
    private Bitmap? thumbnailBitmap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private ChannelSlotState state;

    public string StateLabel => State switch
    {
        ChannelSlotState.Empty => "Empty",
        ChannelSlotState.Loaded => "Loaded",
        ChannelSlotState.Aligned => "Aligned",
        ChannelSlotState.Retouched => "Retouched",
        ChannelSlotState.Result => "Result",
        _ => "—",
    };

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
        ThumbnailBitmap?.Dispose();
        ThumbnailBitmap = null;
    }

    partial void OnImageChanged(ImageBuffer? value)
    {
        RefreshDisplayBitmap();
    }

    partial void OnResultChanged(RgbImageBuffer? value)
    {
        RefreshDisplayBitmap();
    }

    internal void ApplyEditedImage(ImageBuffer image)
    {
        if (!ReferenceEquals(Image, image))
        {
            Image = image;
            return;
        }

        RefreshDisplayBitmap();
    }

    internal void ApplyEditedResult(RgbImageBuffer rgb)
    {
        if (!ReferenceEquals(Result, rgb))
        {
            Result = rgb;
            return;
        }

        RefreshDisplayBitmap();
    }

    private void RefreshDisplayBitmap()
    {
        Bitmap? newDisplay;
        Bitmap? newThumbnail;

        if (Result is not null)
        {
            newDisplay = AvaloniaBitmapFactory.FromRgbImageBuffer(Result);
            newThumbnail = AvaloniaBitmapFactory.CreateThumbnail(Result);
        }
        else if (Image is not null)
        {
            newDisplay = AvaloniaBitmapFactory.FromImageBuffer(Image);
            newThumbnail = AvaloniaBitmapFactory.CreateThumbnail(Image);
        }
        else
        {
            newDisplay = null;
            newThumbnail = null;
        }

        var previousDisplay = DisplayBitmap;
        var previousThumbnail = ThumbnailBitmap;
        DisplayBitmap = newDisplay;
        ThumbnailBitmap = newThumbnail;
        previousDisplay?.Dispose();
        previousThumbnail?.Dispose();
    }
}
