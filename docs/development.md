# Development Guide

## Repository Layout

```text
Prokudin.slnx
Directory.Build.props
Directory.Packages.props
src/
  Prokudin.Core/
  Prokudin.Cli/
  Prokudin.Gui/
tests/
  Prokudin.Core.Tests/
  Prokudin.Gui.Tests/
docs/
```

The Python implementation has been removed. New development should target the
.NET projects.

## Build

```powershell
dotnet build Prokudin.slnx
```

## Test

```powershell
dotnet test Prokudin.slnx
```

The test suite covers:

- triptych split and segment size normalization
- typed `ImageBuffer` round-trip and format conversion
- color correction and per-channel exposure
- crop and `AlignedChannelCropper`
- synthetic reconstruction pipeline
- OpenCvSharp alignment (small and large synthetic shifts)
- `MaxTranslation` clamping and `ResolveMaxTranslation` auto-scale
- retouch: auto-clean detection, clone stamp, brush masks
- cross-channel guided healing (`CrossChannelHealerTests`)
- GUI: `MainViewModel` workflow, export settings store, preview geometry

Alignment regression highlights in `ChannelAlignerTests`:

- `AlignChannel_AlignsShiftedSyntheticChannel` — 7×-5 px shift
- `AlignChannel_AlignsLargeArchivalShift_WhenMaxTranslationAllows` — ~18×-78 px
- `AlignChannel_RejectsLargeArchivalShift_WhenMaxTranslationTooSmall` — 48 px cap
- `ResolveMaxTranslation_UsesDefaultOrAutoScale` — formula verification

Manual validation for archival scans: use a local LoC TIFF (not committed) with
CLI `--triptych-order bgr` and compare channel shifts before/after reconstruction.

## Run CLI

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct red.png green.png blue.png -o output.png
```

Archival triptych:

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct --triptych scan.tif --triptych-order bgr -o output.png --max-translation 128
```

## Run GUI

```powershell
dotnet run --project src\Prokudin.Gui\Prokudin.Gui.csproj
```

## Package Management

Package versions are centralized in `Directory.Packages.props`.

Important packages:

- OpenCvSharp4
- OpenCvSharp4.runtime.win
- SixLabors.ImageSharp
- Avalonia
- Avalonia.Desktop
- Avalonia.Themes.Fluent
- CommunityToolkit.Mvvm
- xUnit
- FluentAssertions

## Known Warnings

`dotnet test` can report `NU1903` for Avalonia's transitive
`Tmds.DBus.Protocol` dependency.

Avalonia drag/drop APIs used by the first GUI pass emit obsolete API warnings.
The app builds and runs; modern `DataTransfer` drag/drop can be adopted later.

## Follow-Up Work

- Add golden comparison tests using archived sample channels.
- Expose `MaxTranslation` and manual alignment nudge in the Avalonia UI.
- Validate OpenCvSharp native runtime packages for Linux and macOS.
- Add publish profiles or CI jobs for Windows first.
- Expose loupe, pipette balance, and temperature/tint controls in Avalonia.
