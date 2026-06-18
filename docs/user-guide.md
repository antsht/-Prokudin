# User Guide

## Input Modes

Prokudin accepts either three separate grayscale channel files or one triptych.

Supported file extensions:

- `.png`
- `.jpg`
- `.jpeg`
- `.tif`
- `.tiff`

### Triptych layout

Triptychs are split along the long side into three segments:

- **Horizontal** image: three columns (left, middle, right).
- **Vertical** image: three rows (top, middle, bottom).

After split, segments are cropped to a common width and height from the top-left
so all channels match before alignment.

Triptych orders:

| Order | Segment 1 | Segment 2 | Segment 3 |
| --- | --- | --- | --- |
| `rgb` | Red | Green | Blue |
| `bgr` | Blue | Green | Red |

**Library of Congress** Prokudin-Gorskii TIFFs are usually vertical triptychs.
Use **BGR** (the GUI default). RGB order is for sources that store red first.

## Desktop App

Start the GUI:

```powershell
dotnet run --project src\Prokudin.Gui\Prokudin.Gui.csproj
```

### Basic workflow

1. Open R, G, and B channel files, or open a triptych and choose `RGB` or `BGR`.
2. Use the left thumbnail list to inspect channels.
3. Drag one R/G/B thumbnail onto another to swap channel assignments.
4. Run **Auto-align**.
5. Read the status bar for alignment details (transform type, inliers, shifts).
6. Inspect the result preview.
7. Export the result as PNG, JPEG, or TIFF.

### Status bar after auto-align

When alignment finishes, the status line includes metadata for each channel, for
example:

```text
Auto-align complete. Result is 3081 x 3081. Red: homography (420 inliers, shifts (1.2,-0.3)); Green: reference (0 inliers); Blue: homography (395 inliers, shifts (0.1,0.2))
```

Use this to confirm alignment ran correctly:

| `Kind` | Meaning |
| --- | --- |
| `reference` | Fixed reference channel (default green) |
| `homography` | Good feature match; preferred outcome |
| `affine` | Partial affine fit |
| `translation` | Median translation fallback |
| `identity` | No shift applied; check order or increase shift limit |

If red or blue show `identity` with visible color fringing, the shift may exceed
`MaxTranslation`. Use the CLI `--max-translation` flag with a higher value or `0`
for auto-scale (GUI uses the default 128).

### Current GUI scope

- channel loading
- triptych loading with RGB/BGR selector
- thumbnail swap
- auto-align with status metadata
- result preview
- PNG, JPEG, and TIFF result export with saved export settings

Manual nudge, crop overlay, loupe, alignment limit control, and full color
controls are not yet exposed in the Avalonia UI.

## Command Line

### Separate channel files

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct red.png green.png blue.png -o output.png
```

### Triptych (archival scan example)

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct --triptych scan.tif --triptych-order bgr -o output.png
```

### Alignment tuning

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct `
  --triptych scan.tif --triptych-order bgr -o output.png `
  --max-translation 0 `
  --reference green --detector sift
```

### CLI options

| Flag | Description |
| --- | --- |
| `-o`, `--output` | Output PNG path (required) |
| `--triptych PATH` | Single stacked triptych instead of three files |
| `--triptych-order rgb\|bgr` | Segment order; requires `--triptych` |
| `--reference red\|green\|blue` | Alignment reference channel (default `green`) |
| `--detector sift\|orb` | Feature detector (default `sift`) |
| `--max-align-iter N` | Fine alignment iterations (default `3`) |
| `--max-translation N` | Per-axis shift limit in pixels (default `128`; `0` = auto-scale) |
| `--size N` | Square output size `N × N` |
| `--no-trim-borders` | Disable dark border trim |
| `--no-sharpen` | Disable unsharp mask |

`--triptych-order` is valid only with `--triptych`.

## Troubleshooting

### Color fringing after auto-align

1. Confirm triptych order. LoC scans usually need **BGR**.
2. Check the status bar or rerun via CLI. `identity` on red/blue means no shift
   was applied.
3. Increase `--max-translation` (try `0` for auto-scale, or `150`–`200` for
   difficult scans).
4. Try `--detector orb` if SIFT inlier counts are very low.

### Wrong colors but sharp edges

Usually wrong triptych order. Swap between RGB and BGR.

### Cropped result smaller than expected

Normal. The pipeline crops to the three-way channel overlap, then the largest
centered square inside that region.
