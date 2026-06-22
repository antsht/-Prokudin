# Prokudin RGB Reconstruction

Native .NET 10 tool for reconstructing a color image from three grayscale
red, green, and blue channel photographs in the Prokudin-Gorskii workflow.

The project does not use neural restoration. It performs image loading,
geometric alignment, channel merging, crop, color correction, per-channel
retouch (heal brush, clone stamp, auto-clean), and image export.

## Historical Source

This project is made for aligning and reconstructing photographs taken by
Sergei Mikhailovich Prokudin-Gorskii. His color documentary survey of the
Russian Empire was made mainly in 1909-1915 using color separation negatives:
three black-and-white frames photographed through red, green, and blue filters.

The archive was digitized and published by the U.S. Library of Congress as the
[Prokudin-Gorskii Collection](https://www.loc.gov/pictures/collection/prok/).
The Library describes the collection as 2,607 distinct images, including 1,902
black-and-white triple-frame glass negatives and modern digital color composites
made from those negatives.

## Projects

| Project | Purpose |
| --- | --- |
| `src/Prokudin.Core` | Image I/O, triptych split, alignment, crop, color, retouch, pipeline |
| `src/Prokudin.Cli` | Command-line reconstruction |
| `src/Prokudin.Gui` | Avalonia desktop app |
| `tests/Prokudin.Core.Tests` | Core regression tests |
| `tests/Prokudin.Gui.Tests` | GUI viewmodel and service tests |

## Requirements

- .NET SDK 10
- Windows for the currently wired OpenCvSharp native runtime package
- PNG, JPEG, TIFF input images

OpenCV alignment is implemented through OpenCvSharp. The current native runtime
package is Windows-specific. Linux and macOS publishing still need RID-specific
OpenCV runtime validation before they should be treated as supported release
targets.

## Build And Test

```powershell
dotnet test Prokudin.slnx
dotnet build src\Prokudin.Gui\Prokudin.Gui.csproj
```

`dotnet test` currently emits `NU1903` for Avalonia's transitive
`Tmds.DBus.Protocol` dependency. The warning is known; tests still pass.

## GUI

```powershell
dotnet run --project src\Prokudin.Gui\Prokudin.Gui.csproj
```

The desktop app supports:

- opening separate R, G, B channel images
- opening a stacked triptych with RGB or BGR order (BGR for Library of Congress scans)
- sidebar channel cards with thumbnails for R, G, B, and the result
- drag/drop swap between R, G, B slots
- auto-align with alignment metadata in the status bar
- heal brush and clone stamp on aligned channels
- cross-channel guided healing (default on) and per-channel auto-clean masks
- crop-to-selection, per-channel exposure, auto white balance, undo/redo
- result preview and PNG/JPEG/TIFF export with saved export settings
- export prepared aligned channels after auto-align

For vertical LoC TIFF triptychs, use **BGR** order.

## CLI

Three separate channel files:

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct red.png green.png blue.png -o output.png
```

Single triptych:

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct --triptych scan.tif --triptych-order bgr -o output.png
```

Supported options:

| Flag | Description |
| --- | --- |
| `-o`, `--output` | Output PNG path. Required. |
| `--triptych PATH` | Load one stacked triptych instead of three channel files. |
| `--triptych-order rgb\|bgr` | Segment order for triptych input. Requires `--triptych`. |
| `--size N` | Resize output to `N x N`. |
| `--reference red\|green\|blue` | Alignment reference channel. Default: `green`. |
| `--detector sift\|orb` | Feature detector. Default: `sift`. |
| `--max-align-iter N` | Fine alignment iterations. Default: `3`. |
| `--max-translation N` | Per-axis alignment shift limit in pixels. Default: `128`. Use `0` for auto-scale. |
| `--no-trim-borders` | Disable black border trim. |
| `--no-sharpen` | Disable unsharp mask. |

## Documentation

- [User Guide](docs/user-guide.md)
- [Architecture](docs/architecture.md)
- [Core API Reference](docs/core-api.md)
- [Development Guide](docs/development.md)
- [Cross-Channel Guided Healing](docs/cross-channel-healing.md)
