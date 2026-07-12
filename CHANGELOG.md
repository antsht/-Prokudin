# Changelog

All notable changes to Prokudin are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.14.2] - 2026-07-12

### Added

- Colour-correction glossary and ADR defining non-destructive Master/Channel Levels, white-balance sources, white-pick interaction, and correction order.

## [0.14.1] - 2026-07-12

### Added

- Repository agent-skills configuration for GitHub Issues, default triage labels, and single-context domain documentation.

## [0.14.0] - 2026-06-30

### Added

- RAM-aware undo history byte-budget eviction, while retaining the existing 20-entry cap.
- Scalar-only parameter mementos for coalesced color edits so exposure, white balance, and levels undo no longer clone image buffers.
- Design spec `docs/superpowers/specs/2026-06-30-resource-lifetime-and-undo-memory-design.md`.

### Changed

- Snapshot undo mementos no longer store derived RGB results; restore rebuilds the result from `lastAligned` and current color settings.

### Fixed

- Repeated retouch operations no longer create fresh GPU compute contexts for every diagnostics-enabled heal.
- `FallbackImageComputeBackend.Dispose()` now disposes owned child backends while preserving shared process-wide leaves.
- Affine alignment no longer leaks the native OpenCV `Mat` on successful affine transform estimation.
- View menu **Loupe** now toggles loupe mode instead of only updating the menu checkmark.

## [0.13.1] - 2026-06-30

### Fixed

- Healing brush and Telea auto-clean no longer blacken 16-bit channels after retouching.

## [0.13.0] - 2026-06-29

### Added

- GUI keyboard shortcuts: channel/result preview `1`–`4` (top-row `D1`–`D4` and numpad), retouch tools
  `H` / `C` / `M` with workflow switching; updated Help dialog and user guide
- Design spec `docs/superpowers/specs/2026-06-29-keyboard-shortcuts-design.md`

## [0.12.1] - 2026-06-28

### Fixed

- CI on `ubuntu-latest`: guard `Thread.SetApartmentState(STA)` in `AvaloniaTestHost`
  so headless GUI tests run on Linux

## [0.12.0] - 2026-06-27

### Added

- GUI editor command layer: `EditorSession`, `EditorHistory`, `SnapshotCommand`,
  `CoalescedParameterCommand` under `src/Prokudin.Gui/Editing/`
- `MainViewModel` workflow partials (Import, Align, Crop, Clean, Color, History)
- GUI tests: `EditorSessionTests`, `EditorHistoryTests`, `MainViewModelTests` (undo/coalesce)
- Avalonia headless test host for GUI unit tests
- Design spec `docs/superpowers/specs/2026-06-27-editor-command-refactor-design.md`

### Changed

- Levels adjustments are undoable (coalesced with other color edits, 700 ms window)
- Dead code: removed unused `AccelerationBackendKind.CudaAvailable` alias
- `docs/architecture.md` and `AGENTS.md` document the editor command layer

### Fixed

- Auto-clean mask detection stays disabled after opening a saved aligned project (restore prepared `lastAligned` on load)

## [0.11.0] - 2026-06-26

### Added

- Project save/load: folder with `project.json` plus Deflate TIFF channels and RGB result
- Autosave to `%LocalAppData%/Prokudin/autosave/` (default every 10 minutes, configurable)
- Welcome dialog on startup: recover autosave, three recent projects, new/open
- **Edit → Settings** for theme, autosave interval, and processing diagnostics
- Design spec `docs/superpowers/specs/2026-06-26-project-save-design.md`
- `ImageLoader.SaveGrayscaleTiffAsync` for lossless 8/16-bit channel export

### Changed

- **File** menu: New/Open/Save project, Open Recent, keyboard shortcuts (Ctrl+N/O/S)
- Window title shows project name and dirty-state indicator
- Exit and New Project prompt to save unsaved changes

## [0.10.0] - 2026-06-25

### Added

- Two-tier English tooltips for workflow inspector parameters (short hover tip + detailed **?** help)
- `InspectorParameterLabel` and `InspectorParameterCheckBox` controls with `Themes/InspectorTooltips.axaml` resource strings
- `InspectorTipProperties.LongHelp` for screen-reader help on input controls
- Design spec `docs/superpowers/specs/2026-06-25-inspector-tooltips-design.md`

### Changed

- Import, Align, Crop, Clean, Color, and Export inspectors use shared help controls for all adjustable parameters

## [0.9.0] - 2026-06-24

### Added

- GitHub Actions CI (Windows + Linux) and release workflow for `win-x64` and `linux-x64`
- Windows installer (Inno Setup), portable GUI zip, and CLI zip
- Linux AppImage, portable GUI tar.gz, and CLI tar.gz
- Help → Check for updates (GitHub Releases API, opens release page)
- RID-specific OpenCvSharp native runtimes (`win` + `linux-x64.slim`)
- Packaging scripts under `packaging/` and release maintainer guide `docs/release.md`

### Changed

- Published GUI binary name: `Prokudin.exe` / `Prokudin`
- Published CLI binary name: `prokudin.exe` / `prokudin`

## [0.8.0] - 2026-06-24

### Added

- Workflow-based restoration workspace with vertical toolbar (Import, Align, Crop, Clean, Color, Export)
- Context command bars and right-side workflow inspectors
- Manual levels and gamma via `LevelsSettings` in the Color inspector
- Light, dark, and system theme with persistence (`JsonUiSettingsStore`)
- Panel visibility and layout persistence; About dialog and keyboard shortcuts help
- Processing log panel with splitter and status bar

### Changed

- Main window layout rebuilt around workflow navigation instead of a single flat panel

## [0.7.0] - 2026-06-23

### Added

- Auto-clean quality mode profiles and GUI preset with persistence
- Fast auto-clean mode using prediction and Telea fallback
- GPU high-pass backend for auto-clean defect detection
- Coarse-to-fine patch donor search with tile planner
- Tile-grouped parallel healing with lock-free merge
- Reuse of auto-clean detect normalization during apply

### Changed

- Processing log text box: auto-scroll, synchronization, trim, and usability refactor

### Fixed

- Soft fast path allowed for large auto-clean masks

## [0.6.0] - 2026-06-23

### Added

- Processing diagnostics core types and compute-backend fallback logging
- Diagnostics threaded through settings, `PixelParallel`, alignment, pipeline, exposure, and retouch
- GUI processing diagnostics toggles with persisted settings

## [0.5.0] - 2026-06-23

### Added

- ILGPU integration for accelerated image processing
- CUDA auto-clean paths
- Scratch buffer optimizations in the healing pipeline
- Prepared auto-clean masks workflow

### Changed

- Core image processing parallelized (`PixelParallel`)

## [0.4.0] - 2026-06-22

### Added

- Cross-channel guided healing with Telea and patch fallback
- Channel thumbnails (512 px max side) in sidebar cards
- White-balance pipette picker and auto-clean progress reporting

## [0.3.0] - 2026-06-21

### Added

- Dust and scratches auto-removal toolset (auto-clean)
- Brush settings affecting auto-masking
- Zafiro.Avalonia.Icons.Optris icon set

## [0.2.0] - 2026-06-18

### Added

- Channel retouch workflow: heal brush and clone stamp
- Prepared aligned channels kept after auto-align for retouch
- Export settings with persisted configuration
- Visual retouching tools in the GUI

## [0.1.0] - 2026-06-18

### Added

- Initial .NET solution: Core pipeline, CLI, and Avalonia GUI
- Triptych split with RGB/BGR order; channel alignment (SIFT/ORB, homography, affine)
- Crop-to-selection and overlap crop
- Zafiro.Avalonia layout integration
- White balance, exposure, merge, and PNG export
