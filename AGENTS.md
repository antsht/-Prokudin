# AGENTS.md

## Product Facts

Prokudin is a .NET 10 desktop and CLI tool for reconstructing RGB color images
from grayscale red, green, and blue channel scans in the Prokudin-Gorskii
workflow.

The product is technical image reconstruction, not generative restoration. Do
not add neural enhancement, inpainting, cloud processing, or batch workflows
unless explicitly requested.

## Current Stack

- Runtime: .NET 10
- Language: C# 14
- GUI: Avalonia 12 with Zafiro.Avalonia layout controls and CommunityToolkit.Mvvm
- Image I/O: SixLabors.ImageSharp
- Alignment: OpenCvSharp4
- Tests: xUnit plus FluentAssertions

Python code has been removed. Do not reintroduce Python as an implementation
dependency.

## Solution Structure

```text
src/Prokudin.Core        Core image processing and reconstruction pipeline
src/Prokudin.Cli         CLI wrapper over Core
src/Prokudin.Gui         Avalonia desktop UI
tests/Prokudin.Core.Tests
tests/Prokudin.Gui.Tests
docs/
```

## Core Pipeline

The reconstruction flow is:

1. Load three grayscale channels or split a triptych.
2. Trim dark borders if enabled.
3. Align channels to the configured reference channel.
4. Apply manual transforms when provided.
5. Merge R, G, B.
6. Merge partial-edge pixels as grayscale; crop black borders only.
7. Apply white balance, exposure, and levels/gamma (`LevelsSettings`).
8. Resize if requested.
9. Apply unsharp mask unless disabled.
10. Save PNG.

## Alignment Facts

`Prokudin.Core.Alignment.ChannelAligner` uses OpenCvSharp:

- SIFT default detector
- ORB fallback on low SIFT inlier ratio
- homography, affine, then median translation fallback
- phase correlation fine alignment
- ECC translation refinement
- validity masks for overlap crop

`AlignOptions.MaxTranslation` limits per-axis alignment shifts (default **128**;
set to **0** for auto-scale `clamp(minDim × 0.04, 96, 256)`). Archival LoC
triptychs often need 50–100 px shifts; values near 48 px reject valid SIFT
homographies. Must remain meaningful and tested.

`TriptychSplitter` normalizes segment sizes to `min(width)` × `min(height)` after
split so channels are not resized during alignment.

## GUI Facts

The Avalonia GUI is a workflow-based restoration workspace (not a wizard). Default
window size is **1280×800** (min 920×560).

### Layout

Six-row shell: menu → context command bar → workspace → log splitter → processing
log → status bar.

Workspace columns: **channels** (resizable, default 260 px) | splitter |
**workflow toolbar** (88 px) | **preview** | splitter | **right inspector**
(resizable, default 360 px).

Panel sizes, visibility, theme, selected workflow, and autosave options persist in
`%LocalAppData%/Prokudin/ui-settings.json` via `JsonUiSettingsStore`. Export,
auto-clean, and diagnostics settings use separate JSON files in the same folder.
Recent project paths: `recent-projects.json`. Autosave slot: `autosave/`.

### Projects

- **Save Project / Save Project As** writes a folder with `project.json` plus Deflate TIFF
  `red.tif`, `green.tif`, `blue.tif`, and `result.tif` (current working state only).
- **Autosave** uses the same format in `%LocalAppData%/Prokudin/autosave/` (single slot,
  default 10 min interval). Does not clear the dirty flag; explicit Save is still required.
- **Welcome dialog** on startup: recover autosave, open one of three recent projects, or new/open.
- **Edit → Settings**: theme, autosave enable/interval, processing diagnostics.
- Undo/redo is not persisted in project files. `lastAligned` is not restored on load.
- Implementation: `Services/Project/` (`JsonProjectStore`, `ProjectStateMapper`, etc.).
  Design spec: `docs/superpowers/specs/2026-06-26-project-save-design.md`.

### Workflows

Vertical toolbar: **Import**, **Align**, **Crop**, **Clean**, **Color**, **Export**.
Switching workflows is never blocked during operations. Each workflow has a
context command bar (quick actions) and a right **inspector** (detailed parameters).

### Features

- open R/G/B images or triptych with RGB/BGR order (default **BGR** for LoC scans)
- sidebar channel cards with thumbnails (512 px max side), state badges
  (Empty/Loaded/Aligned/Retouched/Result), drag/drop swap R↔G↔B
- auto-align with per-channel metadata; prepared aligned channels kept for retouch
- per-channel heal brush, clone stamp, auto-clean mask (Sensitivity slider in inspector)
- cross-channel guided healing (default on) with Telea/Patch fallback
- crop-to-selection, crop overlap, square selection lock; result crop syncs prepared R/G/B
- auto white balance, pipette picker, per-channel exposure (−2…+2 stops)
- manual levels/gamma via `LevelsSettings` in Color inspector
- fit-to-window / 1:1 preview zoom
- project save/load (folder + `project.json` + TIFF channels/result) and timed autosave
- welcome screen (autosave recovery, three recent projects)
- Edit → Settings for theme, autosave, diagnostics
- PNG/JPEG/TIFF export with persisted settings; export prepared channels
- View menu: Light/Dark/System theme, panel visibility toggles
- Help menu: user guide, keyboard shortcuts, check for updates, About dialog

Keep GUI actions asynchronous for image load, alignment, and export. Avoid doing
large image work on the UI thread. Context bar controls bind `IsUiEnabled` when
they lack a `CanExecute` gate; command buttons disable via `RelayCommand` rules.

`ChannelSlotViewModel` owns cached display and thumbnail bitmaps and disposes
replaced bitmaps. Thumbnails are built in `AvaloniaBitmapFactory.CreateThumbnail`.
Do not reintroduce binding converters that allocate new bitmaps on every binding
conversion.

`ChannelHealer.HealChannel` is the retouch entry point. Cross-channel mode uses
aligned sibling channels as guides; when guides are unavailable it falls back to
Telea with a status message. Healing runs on `Task.Run` from the GUI.

GUI layout uses Zafiro semantic containers (`HeaderedContainer`, `EdgePanel`,
`Card`) and shared theme resources under `src/Prokudin.Gui/Themes/`. ViewModels
stay on CommunityToolkit.Mvvm; ReactiveUI is a transitive Zafiro dependency only.

## CLI Facts

CLI supports:

```powershell
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct red.png green.png blue.png -o output.png
dotnet run --project src\Prokudin.Cli\Prokudin.Cli.csproj -- reconstruct --triptych scan.tif --triptych-order bgr -o output.png
```

`--triptych-order` is valid only with `--triptych`. `--max-translation` sets the
per-axis alignment shift limit (default `128`; `0` = auto-scale).

## Build And Test Commands

```powershell
dotnet test Prokudin.slnx
dotnet build src\Prokudin.Gui\Prokudin.Gui.csproj
```

CI: `.github/workflows/ci.yml` runs tests on `ubuntu-latest` and `windows-latest` for push/PR.

## Distribution Facts

- **Releases:** GitHub Releases via `.github/workflows/release.yml` on `v*` tags.
- **Phase 1 platforms:** `win-x64`, `linux-x64` (GUI + CLI).
- **Windows artifacts:** Inno Setup installer, portable GUI zip, CLI zip.
- **Linux artifacts:** AppImage, portable GUI tar.gz, CLI tar.gz.
- **Publish:** self-contained single-file when `-r` is set (`packaging/Directory.Build.props`).
- **Binary names:** GUI `Prokudin` / `Prokudin.exe`; CLI `prokudin` / `prokudin.exe`.
- **Update check:** Help → Check for updates → GitHub `releases/latest` → open release page (`GitHubReleaseUpdateChecker`).
- **Maintainer guide:** `docs/release.md`; design spec `docs/superpowers/specs/2026-06-24-distribution-design.md`.
- **macOS:** not released yet (codesign/notarization deferred).

Known warning: `NU1903` for Avalonia transitive `Tmds.DBus.Protocol`.

## Runtime Caveat

OpenCvSharp native runtimes are RID-specific:

- Windows / `win-x64`: `OpenCvSharp4.runtime.win`
- Linux / `linux-x64`: `OpenCvSharp4.official.runtime.linux-x64.slim`

Do not claim macOS release support until osx runtime packaging and notarization are validated.

## Imaging Facts

`ImageBuffer` stores `UInt8`, `Float32`, or `UInt16` pixels and exposes
normalized `[0, 1]` accessors. `ImageLoader` preserves 16-bit TIFF samples when
present; export and OpenCV paths convert through `ImageMatConverter` as needed.

## Versioning And Changelog

Prokudin follows [Semantic Versioning 2.0](https://semver.org/spec/v2.0.0.html) as
**0.MINOR.PATCH** until the first stable **1.0.0** release.

- **Source of truth:** `<Version>` in `Directory.Build.props` (inherited by all projects).
- **Display:** About dialog reads the Gui assembly version; CLI must stay in sync via the
  same property.

### On Every User-Requested Change

Before finishing work, ask the user explicitly which bump applies:

| Kind | SemVer (while `0.x.y`) | After `1.0.0` | Example |
|------|------------------------|---------------|---------|
| Breaking (incompatible pipeline, settings, or public API) | **MINOR** `0.x.0` | **MAJOR** `x.0.0` | `0.8.0` → `0.9.0` |
| Feature (new capability, backwards-compatible) | **MINOR** `0.x.0` | **MINOR** `0.x.0` | `0.8.0` → `0.9.0` |
| Fix / docs-only (backwards-compatible correction) | **PATCH** `0.0.x` | **PATCH** `0.0.x` | `0.8.0` → `0.8.1` |

Then:

1. Update `<Version>` in `Directory.Build.props`.
2. Add an entry under `## [x.y.z] - YYYY-MM-DD` in `CHANGELOG.md` (Keep a Changelog format).
   Keep a `## [Unreleased]` section at the top while work is in progress.

Do not bump version or changelog for exploratory edits the user did not ask to keep.

## Development Rules

- Keep Core free of GUI dependencies.
- Keep CLI and GUI thin over Core APIs.
- Prefer records and immutable settings objects for pipeline options.
- Add focused tests for alignment, triptych split, crop, color, and pipeline
  behavior when changing Core.
- Do not commit build outputs from `bin/` or `obj/`.
