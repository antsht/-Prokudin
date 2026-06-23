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
2. Use the left sidebar channel cards (with thumbnails) to inspect and select channels.
3. Drag one R/G/B card onto another to swap channel assignments.
4. Run **Auto-align**. Prepared aligned channels are kept for retouch and export.
5. Read the status bar for alignment details (transform type, inliers, shifts).
6. Optionally retouch a channel (heal brush, clone stamp) or review auto-clean masks.
7. Adjust per-channel exposure or toggle **Auto WB** if needed.
8. Inspect the result preview (fit-to-window or 1:1 zoom).
9. Export the result as PNG, JPEG, or TIFF; optionally **Export Channels** for the prepared R/G/B buffers.

### Channel thumbnails

Each sidebar card shows a downscaled thumbnail (max side 512 px) of the loaded
channel or result. Thumbnails update when the underlying image changes. The main
preview pane shows the full-resolution image for the selected slot.

### Retouch tools

Retouch operates on **aligned working channels** after **Auto-align**. Select a
Red, Green, or Blue slot, then choose a tool from the toolbar:

| Tool | Use |
| --- | --- |
| **Heal** | Paint over dust or spots. Uses cross-channel guided healing when enabled. |
| **Clone stamp** | Alt+click to set source, then paint to copy texture from the source. |

Toolbar controls:

| Control | Default | Description |
| --- | --- | --- |
| **Brush** | — | Brush diameter for heal and stamp strokes. |
| **Radius** | 3 | Inpaint/patch radius passed to `HealOptions`. |
| **Merge** | on | Merge nearby auto-clean defects into one healing area. |
| **Gap** | 3 | Maximum nearby-defect merge distance in pixels. |
| **Expand** | 2 | Expand the final auto-clean healing mask in pixels. |
| **Cross-channel** | on | Use aligned sibling channels as healing guides. |
| **Telea** | off | When cross-channel is off, use OpenCV Telea instead of patch healing. |
| **Debug heal** | off | Write healing debug PNGs and auto-clean mask-stage PNGs under the working directory. |

Heal strokes run asynchronously (`Task.Run`). The status bar reports pixel counts
and fallback messages (for example when guides are unavailable).

**Undo** and **Redo** revert channel edits and retouch strokes. Undo history is
cleared when you reload images or run auto-align.

### Crop to selection

1. Enable **Selection mode** in the toolbar.
2. Drag a rectangle on the preview.
3. Click **Crop to selection**.

Cropping a channel trims that working buffer. Cropping the **Result** slot also
crops the prepared R/G/B channels to the same rectangle so retouch and rebuild
stay aligned.

### Exposure and white balance

Per-channel **R**, **G**, and **B** sliders adjust exposure in stops (−2…+2).
**Auto WB** applies automatic white balance when rebuilding the RGB result.
**Reset exposure** restores all sliders to zero.

### Export settings

Export settings (format, max side, PNG/JPEG/TIFF compression) persist to
`%LocalAppData%\Prokudin\export-settings.json`. Open the settings panel from the
result card or the export toolbar.

### Export channels

After **Auto-align**, use **Export Channels** (File menu or sidebar) to save
the prepared aligned R, G, and B grayscale buffers. Useful for archival handoff or
external editing.

### Auto-clean mask review

Auto-clean works on aligned working channels. Run **Auto-align** first, then
select a Red, Green, or Blue channel and choose **Detect auto-clean mask**.

The app detects dust and scratch candidates by comparing the selected channel
against the other two aligned channels. Detection only creates a mask; it does
not change pixels until you apply it.

While a mask is pending:

- Use **Apply auto-clean mask** to inpaint the selected channel.
- Use **Cancel auto-clean mask** to discard the pending mask.
- Toggle **Review mask on result** to view the same mask over the RGB result.
- Adjust **Agg** to redraw the detected mask live. Higher values include weaker
  dust and scratch candidates; manual mask edits are preserved over the redraw.
- **Radius** sets the heal/inpaint radius used when the mask is applied (shared
  with the heal brush).
- **Merge**, **Gap**, and **Expand** prepare the reviewed healing mask in this
  order: raw auto mask -> merge nearby defects -> expand healing area -> final
  healing mask.
- `Ctrl+click` adds a brush-sized spot to the mask.
- `Alt+click` removes a brush-sized spot from the mask.
- `Ctrl+drag` adds a rectangular mask area.
- `Alt+drag` removes a rectangular mask area.

The mask is cleared if you switch channels, undo/redo, cancel, or change the
working image. Applying the mask uses the same healing options as the heal brush
(cross-channel guided by default). The cleanup repairs only the selected
grayscale channel and leaves the other channels untouched.

### Cross-channel healing

When **Cross-channel** is enabled (default), heal and auto-clean apply use the
two aligned sibling channels as guides. The algorithm fits a local linear model
on a ring around each defect, searches for a matching patch, and blends by
prediction confidence. See [Cross-Channel Guided Healing](cross-channel-healing.md)
for algorithm details.

If guides are unavailable, healing falls back to Telea and the status bar
reports the fallback. Turn **Cross-channel** off to use single-channel healing;
**Telea** selects OpenCV inpaint, otherwise patch healing is used.

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

Implemented in the Avalonia UI:

- channel loading and triptych split with RGB/BGR selector
- sidebar thumbnails and drag/drop slot swap
- auto-align with status metadata and prepared channel retention
- heal brush and clone stamp with cross-channel guided healing
- per-channel auto-clean mask detection, review, editing, apply/cancel
- crop-to-selection on channels and result
- per-channel exposure sliders, auto white balance, undo/redo
- result preview with fit-to-window zoom
- PNG/JPEG/TIFF export with persisted settings; export prepared channels

Not yet exposed: manual alignment nudge, loupe, `MaxTranslation` control, and
full pipette/temperature color controls beyond exposure and auto WB.

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
