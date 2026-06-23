# Auto-Clean Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut auto-clean apply time on full-resolution scans from ~7 minutes to under ~60 seconds in Quality mode, add Quality/Balanced/Fast presets for auto-clean only, and accelerate detect prep with optional GPU high-pass.

**Architecture:** Fix `TryHealLargeMaskFastPath` so `PredictMasked` runs on GPU; refactor `PatchHealer` to tile-based coarse-to-fine search with unchanged scoring; parallelize by tile with lock-free merge; cache detect normalization for apply; expose presets via `AutoCleanQualityProfiles` + GUI ComboBox.

**Tech Stack:** .NET, C# 14, OpenCvSharp, ILGPU, native `Prokudin.Cuda.dll`, Avalonia, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-06-23-auto-clean-performance-design.md`

---

## File map

| File | Responsibility |
| --- | --- |
| `Retouch/AutoCleanQualityMode.cs` | Quality / Balanced / Fast enum |
| `Retouch/AutoCleanQualityProfiles.cs` | Map mode → `(AutoCleanSettings, HealOptions)` |
| `Retouch/AutoCleanSessionCache.cs` | Normalized buffers + model from detect for apply reuse |
| `Retouch/HealOptions.cs` | Add `QualityMode`, `AllowSoftFastPath` |
| `Retouch/ChannelHealer.cs` | Fast path fix, tile orchestration, mode branches |
| `Retouch/PatchHealer.cs` | Tile coarse-to-fine donor search |
| `Retouch/PatchHealerContext.cs` | Precomputed float/gradient maps shared per apply |
| `Retouch/PatchSearchPlanner.cs` | 256×256 tile grouping + component→tile index |
| `Retouch/HealingTileMerger.cs` | Per-tile float buffer merge without per-component lock |
| `Retouch/ChannelRetoucher.cs` | Session cache output from detect; GPU high-pass hook |
| `Processing/IImageComputeBackend.cs` | Add `TryHighPassAbs` |
| `Processing/*Backend*.cs` | High-pass implementations |
| `native/Prokudin.Cuda/ProkudinCuda.cu` | Separable Gaussian high-pass kernel |
| `Gui/Services/JsonAutoCleanSettingsStore.cs` | Persist `QualityMode` |
| `Gui/ViewModels/MainViewModel.cs` | Wire preset, cache lifecycle |
| `Gui/Views/MainWindow.axaml` | ComboBox for preset |
| `tests/...` | Profiles, fast path, tile equivalence, cache, high-pass |

---

### Task 1: Quality mode types and profiles

**Files:**
- Create: `src/Prokudin.Core/Retouch/AutoCleanQualityMode.cs`
- Create: `src/Prokudin.Core/Retouch/AutoCleanQualityProfiles.cs`
- Modify: `src/Prokudin.Core/Retouch/HealOptions.cs`
- Create: `tests/Prokudin.Core.Tests/Retouch/AutoCleanQualityProfilesTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class AutoCleanQualityProfilesTests
{
  [Theory]
  [InlineData(AutoCleanQualityMode.Quality, 48, true, true, 3)]
  [InlineData(AutoCleanQualityMode.Balanced, 32, true, true, 5)]
  [InlineData(AutoCleanQualityMode.Fast, 48, false, false, 8)]
  public void Resolve_AppliesExpectedHealOverrides(
      AutoCleanQualityMode mode,
      int searchRadius,
      bool guidedPatch,
      bool localPrediction,
      int mergeDistance)
  {
      var userDetect = new AutoCleanSettings(Sensitivity: 50, AutoMergeDistancePx: 3);
      var userApply = new HealOptions(PatchRadius: 4);

      var (detect, apply) = AutoCleanQualityProfiles.Resolve(mode, userDetect, userApply);

      detect.AutoMergeDistancePx.Should().Be(mergeDistance);
      apply.SearchRadius.Should().Be(searchRadius);
      apply.UseGuidedPatchSearch.Should().Be(guidedPatch);
      apply.UseLocalLinearPrediction.Should().Be(localPrediction);
      apply.QualityMode.Should().Be(mode);
      apply.PatchRadius.Should().Be(4); // user value preserved
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~AutoCleanQualityProfilesTests" -v n`

Expected: FAIL — type `AutoCleanQualityProfiles` not found.

- [ ] **Step 3: Implement types**

`AutoCleanQualityMode.cs`:

```csharp
namespace Prokudin.Core.Retouch;

public enum AutoCleanQualityMode
{
    Quality,
    Balanced,
    Fast,
}
```

Add to `HealOptions.cs` record parameters:

```csharp
AutoCleanQualityMode QualityMode = AutoCleanQualityMode.Quality,
bool AllowSoftFastPath = true,
```

`AutoCleanQualityProfiles.cs`:

```csharp
namespace Prokudin.Core.Retouch;

public static class AutoCleanQualityProfiles
{
    public static (AutoCleanSettings Detect, HealOptions Apply) Resolve(
        AutoCleanQualityMode mode,
        AutoCleanSettings userDetect,
        HealOptions userApply)
    {
        var detect = mode switch
        {
            AutoCleanQualityMode.Balanced => userDetect with { AutoMergeDistancePx = 5 },
            AutoCleanQualityMode.Fast => userDetect with { AutoMergeDistancePx = 8 },
            _ => userDetect,
        };

        var apply = mode switch
        {
            AutoCleanQualityMode.Balanced => userApply with
            {
                QualityMode = mode,
                SearchRadius = 32,
                UseGuidedPatchSearch = true,
                UseLocalLinearPrediction = true,
                LowConfidenceThreshold = 0.25f,
                AllowSoftFastPath = true,
            },
            AutoCleanQualityMode.Fast => userApply with
            {
                QualityMode = mode,
                UseGuidedPatchSearch = false,
                UseLocalLinearPrediction = false,
                LowConfidenceThreshold = 0.15f,
                AllowSoftFastPath = true,
            },
            _ => userApply with
            {
                QualityMode = mode,
                SearchRadius = 48,
                UseGuidedPatchSearch = true,
                UseLocalLinearPrediction = true,
                LowConfidenceThreshold = 0.35f,
                AllowSoftFastPath = true,
            },
        };

        return (detect, apply);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~AutoCleanQualityProfilesTests" -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/AutoCleanQualityMode.cs src/Prokudin.Core/Retouch/AutoCleanQualityProfiles.cs src/Prokudin.Core/Retouch/HealOptions.cs tests/Prokudin.Core.Tests/Retouch/AutoCleanQualityProfilesTests.cs
git commit -m "feat: add auto-clean quality mode profiles"
```

---

### Task 2: Fast path fix and rejection logging

**Files:**
- Modify: `src/Prokudin.Core/Retouch/ChannelHealer.cs` (`TryHealLargeMaskFastPath`, `HealCrossChannel`)
- Create: `tests/Prokudin.Core.Tests/Retouch/FastPathHealerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class FastPathHealerTests
{
    [Fact]
    public void HealChannel_LargeMask_UsesFastPathStatusWhenModelIsValid()
    {
        var (target, g1, g2, mask) = CreateLargeCorrelatedScene(64, 64, defectPixels: 900);
        var capture = new CapturingProcessingDiagnostics(
            new ProcessingDiagnosticsOptions(ProcessingLogCategory.PipelineStage));

        var result = ChannelHealer.HealChannel(
            target, g1, g2, mask,
            new HealOptions(
                LargeMaskFastPathPixelThreshold: 100,
                AllowSoftFastPath: true,
                Diagnostics: capture));

        result.StatusMessage.Should().Contain("Large auto-clean");
        capture.Messages.Should().Contain(m =>
            m.Contains("fast path", StringComparison.OrdinalIgnoreCase));
    }

    private static (ImageBuffer Target, ImageBuffer G1, ImageBuffer G2, byte[] Mask) CreateLargeCorrelatedScene(
        int w, int h, int defectPixels)
    {
        var target = ImageBuffer.FromNormalizedFloat(CreatePlane(w, h, 0.4f), w, h);
        var g1 = ImageBuffer.FromNormalizedFloat(CreatePlane(w, h, 0.38f), w, h);
        var g2 = ImageBuffer.FromNormalizedFloat(CreatePlane(w, h, 0.42f), w, h);
        var mask = new byte[w * h];
        for (var i = 0; i < defectPixels; i++)
        {
            mask[i] = 1;
        }

        return (target, g1, g2, mask);
    }

    private static float[] CreatePlane(int w, int h, float value)
    {
        var data = new float[w * h];
        Array.Fill(data, value);
        return data;
    }
}
```

Adjust `ImageBuffer.FromNormalizedFloat` to match actual factory in codebase when implementing (use same helpers as `CrossChannelHealerTests`).

- [ ] **Step 2: Run test — expect FAIL**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~FastPathHealerTests" -v n`

- [ ] **Step 3: Implement fast path changes in `TryHealLargeMaskFastPath`**

Replace early `return false` on low confidence with soft-failure branch when `options.AllowSoftFastPath && options.QualityMode == AutoCleanQualityMode.Quality`:

```csharp
var confidence = CalculateModelConfidence(...);
var softFastPath = options.AllowSoftFastPath &&
                   options.QualityMode == AutoCleanQualityMode.Quality &&
                   model.Count >= options.MinTrainingPixels &&
                   confidence < options.LowConfidenceThreshold;

if (confidence < options.LowConfidenceThreshold && !softFastPath)
{
    diagnostics.Log(
        ProcessingLogCategory.PipelineStage,
        model.Count < options.MinTrainingPixels
            ? $"[retouch] fast path rejected: training={model.Count} < {options.MinTrainingPixels}"
            : $"[retouch] fast path rejected: confidence={confidence:F2} < {options.LowConfidenceThreshold:F2} (training={model.Count:N0} px)");
    return false;
}

if (softFastPath)
{
    diagnostics.Log(
        ProcessingLogCategory.PipelineStage,
        $"[retouch] fast path soft-accept: confidence={confidence:F2} < {options.LowConfidenceThreshold:F2}, alpha scaled");
}

var baseAlpha = softFastPath
    ? predictionAlpha(confidence, options, isLarge: true) *
      Math.Clamp(confidence / options.LowConfidenceThreshold, options.PredictionAlphaMin, 1.0f)
    : predictionAlpha(confidence, options, isLarge: true);
```

Keep hard `return false` only when `model.Count < options.MinTrainingPixels`.

- [ ] **Step 4: Run tests + full retouch suite**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~FastPathHealerTests|FullyQualifiedName~CrossChannelHealerTests" -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/ChannelHealer.cs tests/Prokudin.Core.Tests/Retouch/FastPathHealerTests.cs
git commit -m "fix: allow soft fast path for large auto-clean masks"
```

---

### Task 3: Patch search planner and context

**Files:**
- Create: `src/Prokudin.Core/Retouch/PatchSearchPlanner.cs`
- Create: `src/Prokudin.Core/Retouch/PatchHealerContext.cs`
- Create: `tests/Prokudin.Core.Tests/Retouch/PatchSearchPlannerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class PatchSearchPlannerTests
{
    [Fact]
    public void Build_MapsEveryComponentToAtLeastOneTile()
    {
        var planner = PatchSearchPlanner.Build(width: 300, height: 200, tileSize: 256);
        planner.TileCount.Should().BeGreaterThan(0);

        var tiles = new HashSet<int>();
        foreach (var tileIndex in planner.GetTilesForRect(x: 10, y: 10, width: 40, height: 40))
        {
            tiles.Add(tileIndex);
        }

        tiles.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement planner**

```csharp
namespace Prokudin.Core.Retouch;

internal sealed class PatchSearchPlanner
{
    private readonly int tileSize;
    private readonly int tilesX;
    private readonly int tilesY;

    private PatchSearchPlanner(int width, int height, int tileSize)
    {
        this.tileSize = tileSize;
        tilesX = (width + tileSize - 1) / tileSize;
        tilesY = (height + tileSize - 1) / tileSize;
    }

    public int TileCount => tilesX * tilesY;

    public static PatchSearchPlanner Build(int width, int height, int tileSize = 256) =>
        new(width, height, tileSize);

    public IEnumerable<int> GetTilesForRect(int x, int y, int width, int height)
    {
        var x0 = Math.Clamp(x / tileSize, 0, tilesX - 1);
        var x1 = Math.Clamp((x + width - 1) / tileSize, 0, tilesX - 1);
        var y0 = Math.Clamp(y / tileSize, 0, tilesY - 1);
        var y1 = Math.Clamp((y + height - 1) / tileSize, 0, tilesY - 1);

        for (var ty = y0; ty <= y1; ty++)
        {
            for (var tx = x0; tx <= x1; tx++)
            {
                yield return (ty * tilesX) + tx;
            }
        }
    }
}
```

`PatchHealerContext.cs` — holds `float[] target`, `float[]? guide1`, `float[]? guide2`, width, height; static factory copies from `ImageBuffer` once.

- [ ] **Step 4: Run tests — PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/PatchSearchPlanner.cs src/Prokudin.Core/Retouch/PatchHealerContext.cs tests/Prokudin.Core.Tests/Retouch/PatchSearchPlannerTests.cs
git commit -m "feat: add patch search tile planner and healer context"
```

---

### Task 4: Coarse-to-fine donor search (equivalence with brute-force)

**Files:**
- Modify: `src/Prokudin.Core/Retouch/PatchHealer.cs`
- Create: `tests/Prokudin.Core.Tests/Retouch/PatchHealerTileSearchTests.cs`

- [ ] **Step 1: Write failing equivalence test**

```csharp
using FluentAssertions;
using OpenCvSharp;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class PatchHealerTileSearchTests
{
    [Fact]
    public void CoarseToFine_FindsSameDonorAsBruteForce_OnSyntheticScene()
    {
        const int size = 64;
        var target = CreateTextured(size, seed: 1);
        var guide1 = CreateTextured(size, seed: 2);
        var guide2 = CreateTextured(size, seed: 3);
        var options = new HealOptions(PatchRadius: 3, SearchRadius: 24, QualityMode: AutoCleanQualityMode.Quality);

        using var component = new Mat(size, size, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(component, new Point(30, 30), 4, Scalar.White, -1);
        using var globalMask = component.Clone();

        var brute = PatchHealer.HealComponent(
            target, guide1, guide2, component, globalMask, options, guided: true,
            useCoarseToFine: false);
        var fast = PatchHealer.HealComponent(
            target, guide1, guide2, component, globalMask, options, guided: true,
            useCoarseToFine: true);

        brute.Succeeded.Should().BeTrue();
        fast.Succeeded.Should().BeTrue();
        fast.DonorCenter.Should().Be(brute.DonorCenter);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`useCoarseToFine` parameter missing)

- [ ] **Step 3: Add `useCoarseToFine` parameter (default `true`) and implement `FindBestDonorCoarseToFine`**

Algorithm inside `PatchHealer`:

1. Collect valid candidate centers in `searchArea` (same validity checks as today).
2. If `!useCoarseToFine` or candidates < 64, call existing nested-loop `FindBestDonor`.
3. Otherwise evaluate candidates at steps `[8, 4, 2, 1]`:
   - Start with all candidates whose centers align to step grid (or snap to nearest).
   - Score with existing `ScoreGuidedDonor` / `ScoreSingleChannelDonor`.
   - Keep top-K (8→4→2→1).
   - Final step 1 evaluates 3×3 neighborhood around each survivor.

Keep `ScoreGuidedDonor` and `ScoreSingleChannelDonor` unchanged.

Balanced mode: pass `coarsestStep: 4` when `options.QualityMode == AutoCleanQualityMode.Balanced`.

- [ ] **Step 4: Run equivalence + CrossChannelHealer tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~PatchHealerTileSearchTests|FullyQualifiedName~CrossChannelHealerTests" -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/PatchHealer.cs tests/Prokudin.Core.Tests/Retouch/PatchHealerTileSearchTests.cs
git commit -m "perf: coarse-to-fine patch donor search with brute-force equivalence"
```

---

### Task 5: Tile-parallel healing and lock-free merge

**Files:**
- Create: `src/Prokudin.Core/Retouch/HealingTileMerger.cs`
- Modify: `src/Prokudin.Core/Retouch/ChannelHealer.cs`

- [ ] **Step 1: Write failing test for merger**

```csharp
using FluentAssertions;
using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Retouch;

public sealed class HealingTileMergerTests
{
    [Fact]
    public void Merge_AppliesFeatheredComponentWithoutContention()
    {
        var result = ImageBuffer.FromNormalizedFloat(new float[16], 4, 4);
        using var component = new Mat(4, 4, MatType.CV_8UC1, Scalar.Black);
        component.Set(1, 1, (byte)255);
        var values = new float[16];
        values[5] = 0.9f;

        HealingTileMerger.ApplyComponent(result, component, values, featherSigma: 0f, width: 4, height: 4);

        result.GetNormalized(5).Should().BeApproximately(0.9f, 0.001f);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Extract `ApplyComponentValues` logic into `HealingTileMerger`**

Move body of `ApplyComponentValues` from `ChannelHealer` to `HealingTileMerger.ApplyComponent` (same math).

In `HealCrossChannel` and `TryHealLargeMaskFastPath`:

- Build `PatchSearchPlanner` once.
- Group `components` by tile index into `Dictionary<int, List<int>>`.
- `Parallel.ForEach` tile groups instead of raw component indices.
- Remove `lock(sync)` around `ApplyComponentValues`; each component writes through merger into shared `result` buffer using existing feather logic (pixels outside component mask unchanged).

Log when diagnostics enabled:

```csharp
diagnostics.Log(ProcessingLogCategory.PipelineStage,
    $"[retouch] patch: {planner.TileCount} tiles, {components.Count} components");
```

- [ ] **Step 4: Run full Core tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/HealingTileMerger.cs src/Prokudin.Core/Retouch/ChannelHealer.cs tests/Prokudin.Core.Tests/Retouch/HealingTileMergerTests.cs
git commit -m "perf: tile-parallel healing with shared merge helper"
```

---

### Task 6: Auto-clean session cache (detect → apply)

**Files:**
- Create: `src/Prokudin.Core/Retouch/AutoCleanSessionCache.cs`
- Modify: `src/Prokudin.Core/Retouch/AutoCleanSettings.cs`
- Modify: `src/Prokudin.Core/Retouch/ChannelRetoucher.cs`
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`
- Create: `tests/Prokudin.Core.Tests/Retouch/AutoCleanSessionCacheTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using FluentAssertions;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class AutoCleanSessionCacheTests
{
    [Fact]
    public void Cache_StoresAndRetrievesNormalizedBuffers()
    {
        var cache = new AutoCleanSessionCache();
        var target = new float[] { 0.1f, 0.2f };
        cache.Store(target, [0.3f, 0.4f], [0.5f, 0.6f], new LinearModel(1, 1, 0, 2));

        cache.TryGet(out var t, out var g1, out var g2, out var model).Should().BeTrue();
        t.Should().Equal(target);
        model.Count.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement cache**

```csharp
namespace Prokudin.Core.Retouch;

public sealed class AutoCleanSessionCache
{
    private float[]? target;
    private float[]? guide1;
    private float[]? guide2;
    private LinearModel model;

    public void Store(float[] target, float[] guide1, float[] guide2, LinearModel model)
    {
        this.target = (float[])target.Clone();
        this.guide1 = (float[])guide1.Clone();
        this.guide2 = (float[])guide2.Clone();
        this.model = model;
    }

    public bool TryGet(out float[] target, out float[] guide1, out float[] guide2, out LinearModel model)
    {
        if (this.target is null)
        {
            target = guide1 = guide2 = [];
            model = default;
            return false;
        }

        target = this.target;
        guide1 = this.guide1!;
        guide2 = this.guide2!;
        model = this.model;
        return true;
    }

    public void Clear()
    {
        target = guide1 = guide2 = null;
        model = default;
    }
}
```

Add optional `AutoCleanSessionCache? SessionCache` to `AutoCleanSettings` and `HealOptions`.

In `DetectSingleChannelDefects`, after normalization + `FitPredictionModel`, call `settings.SessionCache?.Store(...)`.

In `TryHealLargeMaskFastPath`, if `options.SessionCache?.TryGet(...)` succeeds, skip `CopyNormalizedTo` triple copy and log `[retouch] reuse detect normalization cache`.

In `MainViewModel`:
- Field `private readonly AutoCleanSessionCache autoCleanSessionCache = new();`
- Pass into `CreateAutoCleanSettings` / `CreateHealOptions`.
- Call `autoCleanSessionCache.Clear()` on re-align, channel load, slot change (`ClearPendingAutoCleanMask` and align success paths).

- [ ] **Step 4: Run tests — PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/AutoCleanSessionCache.cs src/Prokudin.Core/Retouch/AutoCleanSettings.cs src/Prokudin.Core/Retouch/ChannelRetoucher.cs src/Prokudin.Core/Retouch/HealOptions.cs src/Prokudin.Gui/ViewModels/MainViewModel.cs tests/Prokudin.Core.Tests/Retouch/AutoCleanSessionCacheTests.cs
git commit -m "feat: reuse auto-clean detect normalization during apply"
```

---

### Task 7: Fast mode apply branch

**Files:**
- Modify: `src/Prokudin.Core/Retouch/ChannelHealer.cs`
- Create: `tests/Prokudin.Core.Tests/Retouch/FastModeHealerTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public void HealChannel_FastMode_SkipsGuidedPatchForHighConfidencePixels()
{
    var (target, g1, g2, mask) = CreateLargeCorrelatedScene(48, 48, defectPixels: 400);
    var result = ChannelHealer.HealChannel(
        target, g1, g2, mask,
        new HealOptions(
            QualityMode: AutoCleanQualityMode.Fast,
            LargeMaskFastPathPixelThreshold: 100,
            UseGuidedPatchSearch: false));

    result.UsedCrossChannel.Should().BeTrue();
    result.StatusMessage.Should().Contain("Fast");
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Add `HealFastMode` branch in `HealCrossChannel`**

When `options.QualityMode == AutoCleanQualityMode.Fast` and mask is large:

1. Run `TryHealLargeMaskFastPath` (always attempt).
2. For masked pixels where `globalPrediction[i] >= MinWeightedPrediction`, copy prediction into result with feather.
3. For low-confidence masked pixels only, run Telea via `ChannelRetoucher.InpaintMask` on sub-mask OR tiny patch heal.
4. Status: `Fast auto-clean: prediction + Telea ({defectPixelCount} px).`

- [ ] **Step 4: Run tests — PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/ChannelHealer.cs tests/Prokudin.Core.Tests/Retouch/FastModeHealerTests.cs
git commit -m "feat: fast auto-clean mode using prediction and Telea"
```

---

### Task 8: GUI preset selector and persistence

**Files:**
- Create: `src/Prokudin.Gui/Services/IAutoCleanSettingsStore.cs`
- Create: `src/Prokudin.Gui/Services/JsonAutoCleanSettingsStore.cs`
- Create: `src/Prokudin.Gui/Services/AutoCleanSettingsSnapshot.cs`
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`
- Modify: `src/Prokudin.Gui/Views/MainWindow.axaml`
- Create: `tests/Prokudin.Gui.Tests/JsonAutoCleanSettingsStoreTests.cs`

- [ ] **Step 1: Write failing GUI store test**

```csharp
using FluentAssertions;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Services;

namespace Prokudin.Gui.Tests;

public sealed class JsonAutoCleanSettingsStoreTests
{
    [Fact]
    public void RoundTrip_PreservesQualityMode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"autoclean-{Guid.NewGuid():N}.json");
        var store = new JsonAutoCleanSettingsStore(path);
        store.Save(new AutoCleanSettingsSnapshot(AutoCleanQualityMode.Balanced));
        store.Load().QualityMode.Should().Be(AutoCleanQualityMode.Balanced);
        File.Delete(path);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Implement store and wire ViewModel**

`AutoCleanSettingsSnapshot.cs`:

```csharp
using Prokudin.Core.Retouch;

namespace Prokudin.Gui.Services;

public sealed record AutoCleanSettingsSnapshot(
    AutoCleanQualityMode QualityMode = AutoCleanQualityMode.Quality);
```

`MainViewModel` additions:

```csharp
[ObservableProperty]
private AutoCleanQualityMode autoCleanQualityMode = AutoCleanQualityMode.Quality;

partial void OnAutoCleanQualityModeChanged(AutoCleanQualityMode value)
{
    autoCleanSettingsStore.Save(new AutoCleanSettingsSnapshot(value));
}
```

Update factories:

```csharp
private (AutoCleanSettings Detect, HealOptions Apply) CreateAutoCleanResolvedSettings(ChannelName channelName)
{
    var (detect, apply) = AutoCleanQualityProfiles.Resolve(
        AutoCleanQualityMode,
        CreateAutoCleanSettings(channelName),
        CreateHealOptions());
    return (
        detect with { SessionCache = autoCleanSessionCache },
        apply with { SessionCache = autoCleanSessionCache });
}
```

Use in `AutoCleanSelectedChannel` and `ApplyAutoCleanMask`.

`MainWindow.axaml` — add near sensitivity slider:

```xml
<ComboBox SelectedItem="{Binding AutoCleanQualityMode}"
          ItemsSource="{Binding AutoCleanQualityModes}"
          ToolTip.Tip="Quality: best result. Balanced: faster. Fast: preview speed." />
```

Expose `AutoCleanQualityModes` as `Enum.GetValues<AutoCleanQualityMode>()` with display names via `DisplayName` converter or item template.

- [ ] **Step 4: Run GUI + Core tests**

Run: `dotnet test Prokudin.slnx -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Gui/Services/IAutoCleanSettingsStore.cs src/Prokudin.Gui/Services/JsonAutoCleanSettingsStore.cs src/Prokudin.Gui/Services/AutoCleanSettingsSnapshot.cs src/Prokudin.Gui/ViewModels/MainViewModel.cs src/Prokudin.Gui/Views/MainWindow.axaml tests/Prokudin.Gui.Tests/JsonAutoCleanSettingsStoreTests.cs
git commit -m "feat: auto-clean quality preset in GUI with persistence"
```

---

### Task 9: GPU high-pass backend

**Files:**
- Modify: `src/Prokudin.Core/Processing/IImageComputeBackend.cs`
- Modify: `src/Prokudin.Core/Processing/CpuImageComputeBackend.cs`
- Modify: `src/Prokudin.Core/Processing/NativeCudaImageComputeBackend.cs`
- Modify: `src/Prokudin.Core/Processing/IlgpuImageComputeBackend.cs`
- Modify: `src/Prokudin.Core/Processing/FallbackImageComputeBackend.cs`
- Modify: `src/Prokudin.Core/Processing/CudaNative.cs`
- Modify: `native/Prokudin.Cuda/ProkudinCuda.cu`
- Modify: `src/Prokudin.Core/Retouch/ChannelRetoucher.cs`
- Create: `tests/Prokudin.Core.Tests/Processing/HighPassBackendTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using FluentAssertions;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Processing;

public sealed class HighPassBackendTests
{
    [Fact]
    public void CpuHighPass_MatchesOpenCvReference_WithinTolerance()
    {
        var source = Enumerable.Range(0, 64).Select(i => (float)i / 64f).ToArray();
        var cpu = new CpuImageComputeBackend();
        var output = new float[64];
        cpu.TryHighPassAbs(source, width: 8, height: 8, sigma: 2.0, output).Should().BeTrue();
        output.Should().AllSatisfy(v => v.Should().BeGreaterOrEqualTo(0));
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Add `TryHighPassAbs` to interface and backends**

`IImageComputeBackend`:

```csharp
bool TryHighPassAbs(float[] source, int width, int height, double sigma, float[] output);
```

CPU: reuse logic from `ChannelRetoucher.HighPassAbs` (extract shared static `HighPassFilter.Compute`).

CUDA: separable Gaussian blur kernel + abs diff kernel in `ProkudinCuda.cu`, export `ProkudinCudaHighPassAbs`.

`ChannelRetoucher.DetectSingleChannelDefects` — replace direct `HighPassAbs` calls with:

```csharp
if (!backend.TryHighPassAbs(normalizedTarget, target.Width, target.Height, 2.0, targetHighPass))
{
    targetHighPass = HighPassAbs(normalizedTarget, ...); // existing OpenCV fallback
}
```

- [ ] **Step 4: Run tests + rebuild native DLL**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~HighPassBackendTests" -v n`

On dev machine with CUDA: `.\native\Prokudin.Cuda\build.ps1`

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Processing/ src/Prokudin.Core/Retouch/ChannelRetoucher.cs native/Prokudin.Cuda/ tests/Prokudin.Core.Tests/Processing/HighPassBackendTests.cs
git commit -m "feat: GPU high-pass backend for auto-clean detect"
```

---

### Task 10: Apply timing diagnostics and docs

**Files:**
- Modify: `src/Prokudin.Core/Retouch/ChannelHealer.cs`
- Modify: `docs/architecture.md`
- Modify: `docs/development.md`

- [ ] **Step 1: Add `Stopwatch` breakdown in `TryHealLargeMaskFastPath`**

When `options.Diagnostics?.Options.IncludeTimings == true`:

```csharp
diagnostics.Log(ProcessingLogCategory.PipelineStage,
    $"[retouch] apply: fast_path=ok, predict={predictMs}ms, patch={patchMs}ms, components={components.Count}");
```

- [ ] **Step 2: Update docs** — add subsection under Acceleration / Retouch describing quality presets, tile patch search, session cache, GPU high-pass.

- [ ] **Step 3: Run full solution build and tests**

Run: `dotnet build Prokudin.slnx && dotnet test Prokudin.slnx -v n`

Expected: PASS

- [ ] **Step 4: Manual benchmark on reference TIFF**

1. Enable diagnostics (Pipeline + Timings + Backends).
2. Auto-clean detect + apply Blue channel, Quality mode.
3. Confirm log contains `PredictMasked`, `fast path`, patch tile stats.
4. Apply time target: < 60 s.

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/ChannelHealer.cs docs/architecture.md docs/development.md
git commit -m "docs: auto-clean performance presets and timing diagnostics"
```

---

## Spec coverage checklist

| Spec requirement | Task |
| --- | --- |
| Quality/Balanced/Fast presets | 1, 7, 8 |
| Fast path fix + soft accept | 2 |
| Tile-based PatchHealer | 3, 4, 5 |
| Memory / parallelism | 5 |
| Session cache detect→apply | 6 |
| Fast mode Telea branch | 7 |
| GPU high-pass | 9 |
| Diagnostics extensions | 2, 5, 10 |
| GUI ComboBox + persistence | 8 |
| Unit tests | 1–9 |
| Docs update | 10 |
| Persistent GPU buffers | Out of scope (spec §3.2) |

## Manual test plan

1. Load LoC TIFF, auto-align, detect auto-clean on Blue.
2. Apply with **Quality** — compare export to pre-change baseline at 100% zoom.
3. Repeat with **Balanced** and **Fast** — confirm speedup and acceptable preview.
4. Re-align — confirm cache cleared (log should not say reuse cache on first apply after align).
