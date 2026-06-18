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
- `SaveRgbAsync(string path, RgbImageBuffer rgb, RgbExportSettings settings, CancellationToken cancellationToken = default)`
- `SavePngAsync(string path, RgbImageBuffer rgb, CancellationToken cancellationToken = default)`

`TrimBlackBorders` removes dark rows and columns up to 2% of width or height per
edge (threshold about 5/255).

`SaveRgbAsync` writes PNG, JPEG, or TIFF output and can optionally resize the
result by maximum side while preserving aspect ratio.

### `TriptychSplitter`

Splits a stacked grayscale image into named channels.

```csharp
IReadOnlyDictionary<ChannelName, ImageBuffer> channels =
    TriptychSplitter.SplitTriptych(image, TriptychOrder.Bgr);
```

Parameters:

- `image`: full triptych as grayscale `ImageBuffer`
- `order`: `TriptychOrder.Rgb` or `TriptychOrder.Bgr`
- `trimBlackBorders`: trim each segment after split (default `true`)

Behavior:

1. Split along the long axis into three segments.
2. Optionally trim dark borders on each segment.
3. Crop every segment to `min(width)` × `min(height)` from the top-left so all
   channels share identical dimensions.

`ParseOrder(string)` accepts `"rgb"` or `"bgr"` for CLI parsing.

## Alignment

### `AlignOptions`

```csharp
public sealed record AlignOptions(
    ChannelName Reference = ChannelName.Green,
    string Detector = "sift",
    int MaxFineIterations = 3,
    bool TrimBorders = true,
    int MaxTranslation = 128);
```

| Member | Description |
| --- | --- |
| `Reference` | Channel kept fixed during alignment |
| `Detector` | `"sift"` or `"orb"` |
| `MaxFineIterations` | Phase-correlation loop cap; scales ECC iterations |
| `TrimBorders` | Trim dark borders on channel load (pipeline paths) |
| `MaxTranslation` | Per-axis shift limit; `0` enables auto-scale |

Static helpers:

```csharp
int limit = AlignOptions.ComputeDefaultMaxTranslation(width, height);
int effective = options.ResolveMaxTranslation(width, height);
```

Auto-scale formula: `clamp((int)(min(width, height) * 0.04), 96, 256)`.

### `AlignChannelMetadata`

Per-channel summary stored on `AlignedChannels.AlignMetadata`:

- `Kind`: `reference`, `homography`, `affine`, `translation`, or `identity`
- `Inliers`: feature inlier count (0 for reference)
- `Shifts`: fine-alignment translation steps, if any

```csharp
string status = AlignChannelMetadata.FormatStatus(aligned.AlignMetadata);
```

### `AlignResult`

Returned by `ChannelAligner.AlignChannel`:

- `Image`: aligned channel
- `Mask`: validity mask for overlap crop
- `TransformKind`: coarse transform type
- `InlierCount`: RANSAC inliers for coarse transform
- `SubpixelShifts`: phase correlation and ECC shifts

### `ChannelAligner`

```csharp
AlignResult result = ChannelAligner.AlignChannel(reference, moving, options);
```

Debug helper:

```csharp
ChannelAligner.SaveAlignmentDebug(reference, alignedRed, alignedBlue, debugDir);
```

Writes edge overlay and R–G difference heatmap PNGs.

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
- `RunAutoAlign(...)` → `AlignedChannels` with `AlignMetadata`
- `ApplyManualToAligned(...)`
- `BuildRgb(...)`

### `AlignedChannels`

Holds aligned red, green, and blue images, per-channel validity masks, and
`AlignMetadata` for diagnostics.

### `PipelineSettings`

Bundles `AlignOptions`, output size, sharpen flag, color options, and manual
transforms.

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
