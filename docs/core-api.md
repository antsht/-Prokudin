# Core API Reference

## Imaging

### `ImageBuffer`

Single-channel float image.

- `Width`
- `Height`
- `Pixels`
- `Crop(...)`
- `Clone()`

Pixel values are expected in `[0, 1]`.

### `RgbImageBuffer`

Three-channel RGB float image.

- `Width`
- `Height`
- `Pixels`
- `Clone()`

Pixel layout is interleaved RGB.

### `ImageLoader`

Key members:

- `SupportedExtensions`
- `IsSupportedImagePath(string path)`
- `LoadGrayscaleAsync(string path, CancellationToken cancellationToken = default)`
- `TrimBlackBorders(ImageBuffer image, float threshold, float maxTrimFraction)`
- `SavePngAsync(string path, RgbImageBuffer rgb, CancellationToken cancellationToken = default)`

### `TriptychSplitter`

Splits a stacked grayscale image into named channels.

```csharp
IReadOnlyDictionary<ChannelName, ImageBuffer> channels =
    TriptychSplitter.SplitTriptych(image, TriptychOrder.Bgr);
```

## Alignment

### `AlignOptions`

Options:

- `Reference`
- `Detector`
- `MaxFineIterations`
- `TrimBorders`
- `MaxTranslation`

### `ChannelAligner`

```csharp
AlignResult result = ChannelAligner.AlignChannel(reference, moving, options);
```

`AlignResult` contains:

- aligned image
- validity mask
- transform kind
- inlier count
- fine alignment shifts

## Pipeline

### `ReconstructionPipeline`

Main entry points:

```csharp
await ReconstructionPipeline.ReconstructFromPathsAsync(
    redPath,
    greenPath,
    bluePath,
    outputPath,
    settings);
```

```csharp
await ReconstructionPipeline.ReconstructFromTriptychAsync(
    triptychPath,
    TriptychOrder.Bgr,
    outputPath,
    settings);
```

Lower-level APIs:

- `LoadProjectChannelsAsync(...)`
- `RunAutoAlign(...)`
- `ApplyManualToAligned(...)`
- `BuildRgb(...)`

## Crop

`Cropper.MergeChannels(...)` creates an RGB image and overlap mask.

`Cropper.SquareCrop(...)` crops to overlap and extracts the largest centered
square.

## Color

`ColorCorrection` provides:

- white balance
- pipette balance
- temperature/tint adjustment
- gentle levels
