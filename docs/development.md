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
- ILGPU
- SixLabors.ImageSharp
- Avalonia
- Avalonia.Desktop
- Avalonia.Themes.Fluent
- CommunityToolkit.Mvvm
- xUnit
- FluentAssertions

## Acceleration Notes

`Prokudin.Core.Processing.PixelParallel` is the shared helper for CPU-bound
managed pixel loops. Use it only when each iteration writes to an independent
index or row. Keep ImageSharp/OpenCV accessor lifetimes and native operations
outside parallel loops unless the code first snapshots the data into managed
arrays.

The internal image compute backend chain detects native CUDA, ILGPU CUDA/OpenCL,
and CPU support. Native CUDA and ILGPU kernels are not required for development
or tests, and every accelerated feature must keep a CPU fallback.

The native DLL exports kernels for auto-clean mask classification and large-mask
bulk prediction during auto-clean apply. ILGPU provides portable C# kernels for
the same operations and normalized exposure gain. Rebuild the native DLL after
changing `native\Prokudin.Cuda\ProkudinCuda.cu`; an older DLL simply makes Core
fall back to ILGPU or CPU paths.

Build the CUDA backend on Windows with:

```powershell
.\native\Prokudin.Cuda\build.ps1
```

The script writes `native\Prokudin.Cuda\bin\Prokudin.Cuda.dll`. Set
`PROKUDIN_CUDA_DLL` to an explicit DLL path if you want to load another build.
Set `PROKUDIN_DISABLE_CUDA=1` to force CPU fallback.

## Processing diagnostics (debug)

Enable toggles above the GUI Processing log:

- **Backends** — which compute backend ran (`DetectDefectMask`, `PredictMasked`, `ApplyGain`) and fallback attempts
- **Pipeline** — alignment coarse/fine paths, exposure rebuild, auto-clean detect/apply
- **CPU parallel** — `PixelParallel` mode inside active scopes
- **Timings** — optional millisecond timings on backend attempts

Settings are stored in `%LocalAppData%/Prokudin/diagnostics-settings.json`.

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
