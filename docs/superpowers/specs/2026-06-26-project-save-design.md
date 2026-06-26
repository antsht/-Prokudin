# Prokudin Project Save & Autosave — Design Spec

**Date:** 2026-06-26  
**Status:** Approved  
**Format version:** 1

## Understanding Summary

- **Goal:** Save/load full working state for multi-day restoration sessions; autosave for crash recovery.
- **Users:** GUI users working on archival R/G/B scans over days/weeks.
- **Format:** Folder with `project.json` + TIFF images (any folder containing `project.json` is a valid project).
- **Contents:** Current channel state (post align/crop/clean) + RGB result + all workflow settings. No aligned layer, no originals, no undo history.
- **Autosave:** Single slot at `%LocalAppData%/Prokudin/autosave/`, default every 10 min (configurable 1–60 min). Independent from named project Save.
- **Startup:** Welcome modal — recover autosave, 3 recent projects, New / Open Other.
- **Settings window:** Global app settings (theme, autosave, diagnostics); export global default + optional override in project on explicit Save.

## Non-Goals

- Cloud sync, collaboration
- Undo/redo in project file
- Aligned layer / original copies
- CLI project support (v1)
- Mandatory `.prokudin` folder suffix

## Decision Log

| # | Decision |
|---|----------|
| 1 | Project folder format |
| 2 | Current channels only (no aligned/originals) |
| 3 | No undo in project |
| 4 | Save `result.tif` with channels |
| 5 | Autosave in `%LocalAppData%/Prokudin/autosave/` |
| 6 | Settings split: project restoration + global export defaults with project override on Save |
| 7 | Welcome modal always on startup |
| 8 | TIFF Deflate lossless, preserve bit depth |
| 9 | Save separate from autosave |
| 10 | Exit: Save / Don't Save / Cancel |
| 11 | Keep autosave after Restore until next autosave |
| 12 | New Project asks if dirty |
| 13 | Any folder with `project.json` |
| 14 | Thin Gui serializer + `ProjectStateMapper` |
| 15 | Autosave does not clear `IsDirty` |
| 16 | Settings under Edit menu |
| 17 | `lastAligned` not restored on load |

## On-Disk Layout

```
{ProjectFolder}/
  project.json
  red.tif
  green.tif
  blue.tif
  result.tif
```

Autosave uses the same layout in `%LocalAppData%/Prokudin/autosave/`.

## Architecture

```
Services/Project/
  ProjectDocument.cs
  ProjectSnapshot.cs
  ProjectStateMapper.cs
  ProjectFileNames.cs
  JsonProjectStore.cs
  IProjectStore.cs
  JsonAutosaveStore.cs
  JsonRecentProjectsStore.cs
  ProjectSaveCoordinator.cs
```

Core: `ImageLoader.SaveGrayscaleTiffAsync` (8/16-bit, Deflate).

## Implementation Phases

1. Core grayscale TIFF save + tests
2. Project format, stores, mapper, unit tests
3. MainViewModel Save/Open/New, dirty, autosave
4. WelcomeDialog, SettingsDialog, File menu
5. App startup flow
6. CHANGELOG + version bump
