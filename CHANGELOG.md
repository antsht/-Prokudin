# Changelog

All notable changes to Prokudin are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
