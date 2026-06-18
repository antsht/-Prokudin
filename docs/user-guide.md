# User Guide

## Input Modes

Prokudin accepts either three separate grayscale channel files or one triptych.

Supported file extensions:

- `.png`
- `.jpg`
- `.jpeg`
- `.tif`
- `.tiff`

Triptychs are split along the long side into three segments. The last segment
keeps any odd-pixel remainder.

Triptych orders:

- `rgb`: segment 1 is red, segment 2 is green, segment 3 is blue
- `bgr`: segment 1 is blue, segment 2 is green, segment 3 is red

## Desktop App

Start the GUI:

```powershell
dotnet run --project src\Prokudin.Gui\Prokudin.Gui.csproj
```

Basic workflow:

1. Open R, G, and B channel files, or open a triptych and choose `RGB` or `BGR`.
2. Use the left thumbnail list to inspect channels.
3. Drag one R/G/B thumbnail onto another to swap channel assignments.
4. Run `Auto-align`.
5. Inspect the result preview.
6. Export PNG.

Current GUI scope:

- channel loading
- triptych loading
- thumbnail swap
- auto-align
- preview
- PNG export

Manual nudge, crop overlay, loupe, and full color controls are not yet exposed
in the Avalonia UI.

## Command Line

Separate channel files:

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct red.png green.png blue.png -o output.png
```

Triptych:

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct --triptych scan.tif --triptych-order bgr -o output.png
```

Notes:

- `--triptych-order` is valid only with `--triptych`.
- `--reference` accepts `red`, `green`, or `blue`.
- `--detector` accepts `sift` or `orb`.
- `--size` creates a square output at the requested size.
