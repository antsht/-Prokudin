# Inspector Parameter Tooltips — Design

Status: **implemented** (2026-06-25)

## Understanding Summary

- **Goal:** Two-tier English tooltips for every adjustable parameter in the right-panel workflow inspectors (Import, Align, Crop, Clean, Color, Export).
- **Why:** The Clean inspector alone exposes ~30 controls; users need to know what each slider, checkbox, and combo affects without reading Core source or `docs/cross-channel-healing.md`.
- **Users:** LoC scan restorers at all skill levels — novices get a one-line hint on hover; advanced users open a detailed explanation via a **?** icon.
- **UX pattern:** Reusable `InspectorParameterLabel` (caption + **?**) with a short `ToolTip` on the label and input control; long text only on **?**.
- **Text storage:** `Themes/InspectorTooltips.axaml` ResourceDictionary, keys `Tips.{Workflow}.{Param}.Short|Long`.
- **Scope:** Workflow inspector parameters only (~48 pairs + ~8 short-only actions/icons). **Out of scope:** Selected header (Channel/Size/Fit), context command bars, workflow toolbar, section header overviews, localization.
- **Non-goals:** Collapsing Debug, changing parameter defaults/behavior, neural features, persisting tooltip preferences.

## Assumptions

| Area | Assumption |
|------|------------|
| Performance | Static strings; no runtime image work |
| Short tip | ≤80 characters, one line |
| Long tip | 2–4 sentences + Higher/Lower guidance + default value + LoC hint where useful |
| Content source | Verified against Core types (`HealOptions`, `AutoCleanSettings`, `AlignOptions`, `LevelsSettings`, etc.) and existing docs |
| CheckBox rows | Same pattern via `InspectorParameterCheckBox` or label column + CheckBox |
| Action buttons | Short tip always; **?** only for non-obvious multi-step actions (Detect mask, Crop overlap) |
| Read-only Import fields | Input mode, source bit depth — no tooltips |
| Accessibility | `AutomationProperties.HelpText` = long tip on the input control |
| Maintenance | New inspector parameter → two resource keys + shared label control |

## Architecture

### Components

1. **`InspectorParameterLabel`** (`Views/Inspector/InspectorParameterLabel.axaml`)
   - Properties: `Caption`, `ShortTip`, `LongTip` (strings or resource keys).
   - Layout: horizontal `[Caption][?]`.
   - **?** button: class `HelpIconButton`, muted 14–16 px, min 24×24 px hit target.
   - Short tip on caption; long tip on **?** only (`MaxWidth` 300, wrap).

2. **`InspectorParameterCheckBox`** (or equivalent)
   - CheckBox + **?** for Healing model, Debug, and similar full-width toggles.

3. **Grid row pattern**
   - Column 0: `InspectorParameterLabel`.
   - Column 1: Slider / NumericUpDown / ComboBox sharing the same short tip.

4. **Styles** (`Themes/Containers.axaml`)
   - `HelpIconButton`, shared tooltip template.

5. **`Themes/InspectorTooltips.axaml`**
   - Merged in `Theme.axaml` alongside Colors/Icons.

### Rejected alternatives

| Alternative | Why rejected |
|-------------|--------------|
| Inline XAML strings only | Hard to maintain at ~100 strings |
| C# static class | User chose ResourceDictionary for Avalonia idioms |
| **?** only on “complex” params | User chose **?** on every adjustable parameter |
| Section header overview **?** | User chose parameter-only tooltips |
| Long tip via hover delay on label | User chose explicit **?** icon |

## Parameter Inventory

### Import (2)

| Parameter | Core / effect |
|-----------|---------------|
| Triptych order | `TriptychSplitter` segment order (LoC often BGR) |
| Trim dark borders | `AlignOptions.TrimBorders` on load/split |

### Align (5)

| Parameter | Core / effect |
|-----------|---------------|
| Reference channel | Fixed reference in `ChannelAligner` |
| Detector | SIFT default, ORB fallback |
| Max translation | `AlignOptions.ResolveMaxTranslation`; 0 = auto `clamp(minDim×0.04, 96, 256)` |
| Max fine iterations | Phase correlation / ECC refinement count |
| Coarse max side | `CoarseAlignmentMaxSide` downscale before feature match |

Alignment metadata summaries are read-only — no tooltips.

### Crop (6 + actions)

| Parameter | Effect |
|-----------|--------|
| X, Y, Width, Height | Selection rect in preview pixels |
| Lock square selection | 1:1 aspect while dragging |
| Selection mode | Marquee on preview (short only) |
| Crop to selection | Crops view; result crop syncs prepared R/G/B (**?** recommended) |
| Crop overlap | Crops aligned channels to largest overlap |
| Reset selection | Clears rect (short only) |

### Clean (~27)

**Retouch:** Brush size, Radius (`InpaintRadius`), Quality (`AutoCleanQualityProfiles`), Sensitivity (detection thresholds), Detect mask.

**Healing model:** Cross-channel guided, Telea vs Patch sub-mode, local linear prediction, guided patch search, robust fit.

**Mask preparation:** Merge nearby defects, merge gap px, expand healing area px, max component area.

**Patch search:** Patch radius, search radius, safety radius, context radius, min training pixels.

**Prediction blend:** Alpha min/max, feather sigma, max allowed error, large component scale.

**Debug:** Debug heal output (`debug/heal/{timestamp}/`), show mask overlay, review mask on result.

See `docs/cross-channel-healing.md` and `HealOptions` / `AutoCleanSettings` for authoritative behavior.

### Color (8)

Auto white balance, WB picker (existing short tip), R/G/B exposure (±2 stops), levels mode, black/white point, gamma (`LevelsSettings`).

### Export (8 + actions)

Format, limit max side, max side, PNG compression, JPEG quality, TIFF compression, deflate level, open folder after export; Export result / Export channels (short tips).

**Volume:** ~48 Short/Long pairs + ~8 short-only ≈ 100 resource strings.

### Example long tip (Max translation)

> Limits how far each channel may shift on X/Y during alignment. **0** uses auto: 4% of the shorter side, clamped 96–256 px. **Default: 128.** Higher values help LoC triptychs with large strip offsets; too low may reject valid matches. Lower values reduce the risk of wrong homographies on difficult scans.

## Content authoring workflow

1. Trace parameter to Core API or pipeline step.
2. Draft Short/Long in `InspectorTooltips.axaml`.
3. Review: short length, Higher/Lower, default, LoC note if applicable.
4. Optional follow-up: one line in `docs/user-guide.md` (not blocking v1).

## Implementation phases

| Phase | Deliverable |
|-------|-------------|
| **1 — Infra** | Controls, styles, ResourceDictionary shell, sample row in AlignInspector |
| **2 — Simple** | Import, Align, Crop, Color, Export (~29 pairs) |
| **3 — Clean** | All Clean sections and texts |
| **4 — Polish** | HelpText, column width pass, `development.md` contributor note |

## Testing

- **Manual:** Each workflow — hover label, control, **?**; verify wrap and delay on sliders.
- **CI:** `dotnet build` + `dotnet test Prokudin.slnx`.
- **Optional:** Gui test that every `TipKey` referenced in XAML exists in the dictionary.

## Risks

| Risk | Mitigation |
|------|------------|
| Label column overflow | Ellipsis + short tip repeats full name; widen column if needed |
| Slider drag vs tooltip | `ShowDelay` ~400 ms on short tips |
| Text drift from Core | PR checklist; comment in resource file pointing to Core type |
| Large diff | Split content commits (Clean texts separate) |

## Decision Log

| # | Decision | Alternatives | Rationale |
|---|----------|--------------|-----------|
| 1 | Two-tier short + long on **?** | Single tooltip; delayed long | Novice + expert audiences |
| 2 | **?** on every adjustable parameter | **?** on complex only | Explicit, consistent |
| 3 | English only | RU; future i18n | Matches UI language |
| 4 | Workflow inspectors only | + context bars; + toolbar | User focus on right panel density |
| 5 | Debug same rules as other params | Simplified; hidden section | Uniformity |
| 6 | No section header **?** | Section overviews | Less visual noise |
| 7 | ResourceDictionary | C# class; inline XAML | Avalonia convention, maintainable |
| 8 | `InspectorParameterLabel` control | Attached properties | ~50 rows need aligned layout |
| 9 | Selected header out of scope | Include Fit to window | Not workflow parameters |
| 10 | Action buttons: short always; **?** for non-obvious | **?** everywhere | Balance density |
| 11 | Read-only Import fields skipped | Tooltips on labels | Not adjustable |
| 12 | Long tips include defaults + LoC hints | Minimal text | Practical guidance |
| 13 | Four implementation phases | Single PR | Clean content dominates effort |
| 14 | SemVer **MINOR** when shipped | PATCH | New UI capability, compatible |

## Versioning

When implemented and released: bump **MINOR** in `Directory.Build.props` and add a `CHANGELOG.md` entry under the release version.
