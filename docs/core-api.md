# Core API Reference

## Imaging

### `ImageBuffer`

Single-channel image with typed pixel storage.

- `Width`, `Height`, `Format` (`PixelFormat.UInt8`, `Float32`, or `UInt16`)
- `Pixels` — direct `float[]` access when `Format == Float32`
- `UInt8Pixels`, `UInt16Pixels` — typed accessors for other formats
- `GetNormalized(int index)`, `SetNormalized(int index, float value)` — `[0, 1]` range
- `CopyNormalizedTo(...)`, `WithFormat(...)`, `FromNormalized(...)`
- `Crop(...)`, `Clone()`, `Filled(...)`
- `MaxAllowedHealError` — format-specific heal error cap

Constructors accept `float[]`, `byte[]`, or `ushort[]` and set `Format`
accordingly.

### `PixelFormat`

```csharp
public enum PixelFormat { UInt8, Float32, UInt16 }
```

### `ImageMatConverter`

OpenCV bridge for retouch and alignment:

- `ToUInt8MatForInpaint(ImageBuffer)`
- `FromMat(Mat, PixelFormat)`

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

### `RgbExportSettings`

```csharp
public sealed record RgbExportSettings
{
    public RgbExportFormat Format { get; init; }           // Png, Jpeg, Tiff
    public int? MaxSide { get; init; }
    public int PngCompression { get; init; }              // 0–9
    public int JpegQuality { get; init; }                 // 1–100
    public TiffExportCompression TiffCompression { get; init; }
    public int TiffDeflateLevel { get; init; }            // 1–9
}
```

`RgbExportSettings.Default` supplies GUI and export defaults. Call `Normalize()`
before save.

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
    int MaxTranslation = 128,
    int CoarseAlignmentMaxSide = 1024);
```

| Member | Description |
| --- | --- |
| `Reference` | Channel kept fixed during alignment |
| `Detector` | `"sift"` or `"orb"` |
| `MaxFineIterations` | Phase-correlation loop cap; scales ECC iterations |
| `TrimBorders` | Trim dark borders on channel load (pipeline paths) |
| `MaxTranslation` | Per-axis shift limit; `0` enables auto-scale |
| `CoarseAlignmentMaxSide` | Downsample max side for coarse SIFT/ORB search; full-resolution warp and fine alignment are still applied |

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

### `AlignedChannelCropper`

Crops prepared aligned channels to overlap rectangles:

```csharp
var (cropped, cropInfo) = AlignedChannelCropper.CropToLargestFullOverlap(aligned);
var croppedOnly = AlignedChannelCropper.Crop(aligned, cropInfo);
```

Used by the GUI after auto-align and when cropping the result selection.

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

`ChannelExposure.Apply(ImageBuffer, float stops)` multiplies normalized pixels
by `2^stops`. It routes through the internal image compute backend chain when
available and preserves CPU fallback behavior. `ChannelExposureSettings` bundles
per-channel stop values for the pipeline and GUI.

## Retouch

### `ChannelHealer`

Primary healing entry point:

```csharp
HealResult result = ChannelHealer.HealChannel(
    targetChannel,
    guideChannel1,   // HealingGuide?; aligned image plus provenance map
    guideChannel2,   // HealingGuide?; aligned image plus provenance map
    defectMask,
    targetProvenance,
    options);
```

`HealResult` contains the healed `ImageBuffer`, the mask used, the updated
per-pixel provenance map, optional `GuidedHealingSummary`, `StatusMessage`,
and `UsedFallback` when cross-channel mode degrades to a conservative local
repair. The legacy image-only overload remains available for callers that do
not retain provenance.

### `HealOptions`

Key defaults:

| Field | Default |
| --- | --- |
| `Mode` | `CrossChannelGuided` |
| `SubMode` | `Patch` |
| `PatchRadius` | 3 |
| `SearchRadius` | 48 |
| `ContextRadius` | 16 |
| `PredictionAlphaMin` / `Max` | 0.15 / 0.75 |
| `MaxComponentArea` | 5000 |
| `DebugOutput` | false |

Normalized radius helpers: `NormalizedPatchRadius`, `NormalizedInpaintRadius`, etc.

### `ChannelRetoucher`

Auto-clean detection prepares its healing mask before review or healing:

```text
raw auto mask -> merge nearby defects -> expand healing area -> final healing mask
```

`ChannelHealer` receives only the final healing mask. Merge and expand operations
modify mask data only; source image channels are not changed during mask
preparation.

`PrepareAutoCleanMask(rawAutoMask, width, height, settings)` is available for
synthetic tests and callers that already have a raw auto-clean mask.

- `InpaintMask(image, mask, radius)` — OpenCV Telea inpaint
- `DetectSingleChannelDefects(target, other1, other2, settings)` — auto-clean mask
- `AutoClean(image, settings)` — legacy detect-and-inpaint
- `CreateBrushMask(width, height, strokes)` — heal brush mask from strokes
- `Stamp(image, CloneStampStroke)` — clone stamp

### `AutoCleanSettings`

Additional auto-clean mask preparation fields:

- `AutoExpandHealingAreaPx` defaults to 2 and is normalized to 0-10 px.
- `AutoMergeNearbyDefects` defaults to true.
- `AutoMergeDistancePx` defaults to 3 and is normalized to 0-20 px.
- `MaxAutoExpandedComponentArea` defaults to 10000; if preparation creates a
  larger component, merge/expand radii are reduced until the component fits or
  both radii reach zero.
- `DebugOutput`, `DebugOutputDirectory`, and `DebugMaskPrefix` control raw,
  merged, expanded, and final mask PNG output.

### `AutoCleanMaskResult`

`Mask` remains the backward-compatible final mask. `CandidatePixels` counts
pixels in the final mask.

```csharp
public sealed record AutoCleanMaskResult(byte[] Mask, int CandidatePixels)
{
    public byte[] RawMask { get; init; }
    public byte[] MergedMask { get; init; }
    public byte[] ExpandedMask { get; init; }
    public byte[] FinalMask { get; init; }
}
```

### `AutoCleanSettings` signature

```csharp
public sealed record AutoCleanSettings(
    int Sensitivity = 50,
    int InpaintRadius = 3,
    int AutoExpandHealingAreaPx = 2,
    bool AutoMergeNearbyDefects = true,
    int AutoMergeDistancePx = 3,
    int MaxAutoExpandedComponentArea = 10000,
    bool DebugOutput = false,
    string? DebugOutputDirectory = null,
    string? DebugMaskPrefix = null);
```

`Sensitivity` is 0–100 (GUI **Agg** slider). `InpaintRadius` is 1–24.
