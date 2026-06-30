# Resource Lifetime & Undo Memory — Design

- Date: 2026-06-30
- Status: Proposed
- Scope: `Prokudin.Core` (compute backends, alignment), `Prokudin.Gui` (editor undo history)
- Related specs: `2026-06-27-editor-command-refactor-design.md`

## 1. Context

A senior review of the current codebase (clean build, 155 tests passing) surfaced
three resource-lifetime issues. They do not affect correctness of a single
operation, but they cause unbounded memory growth during normal editing
sessions, which is the primary concern this design addresses.

The headline driver: users load **40+ MP per-channel** plates, and **graceful
degradation of undo depth under memory pressure is acceptable** (confirmed with
the maintainer). The design is sized for that scale.

### Findings

**P1 — GPU compute backends leak on every heal (active).**
`IImageComputeBackend` is `IDisposable` because `IlgpuImageComputeBackend` owns an
ILGPU `Context` + `Accelerator` (native device resources and worker threads), and
there is a native CUDA backend.

```text
ImageComputeBackendFactory.CreateBest(diagnostics)
  → diagnostics is null/Null  → returns cached Lazy singleton (ok)
  → diagnostics is non-null   → CreateChain(): builds a NEW Context.CreateDefault()
                                + accelerator every call
```

Three call sites take the freshly-built chain and never dispose it:
`ChannelHealer.cs:398`, `ChannelHealer.cs:570`, `ChannelRetoucher.cs:85`. The GUI
always passes a real diagnostics sink (`MainViewModel.CreateHealOptions` →
`Diagnostics: CreateDiagnostics()` → `GuiProcessingDiagnostics`), so **every
auto-clean / heal in the GUI leaks an ILGPU context** (plus a CUDA backend when
present). Repeated retouching accumulates contexts until GPU/host memory
exhaustion.

**P1b — `FallbackImageComputeBackend.Dispose()` is a no-op.** It does not dispose
its child backends (`FallbackImageComputeBackend.cs:87-89`). So disposing the
chain would not have released the leaf contexts anyway; the fix must address leaf
lifetime directly.

**P2 — Undo history stores full-resolution deep clones (dominant memory cost).**
`EditorHistory` keeps up to 20 mementos. Each `EditorMemento` deep-clones R, G, B,
the RGB `Result`, and `LastAligned` (3 channels + 3 masks), regardless of what the
command actually changed. The most frequent edits — exposure / white balance /
levels — are `CoalescedParameterCommand`s that change **only scalars**, yet still
clone every heavy buffer. There is no byte budget; only an entry count cap.

At 40 MP, one full snapshot memento is on the order of **~1 GB** (16-bit channels);
20 deep is multiple GB and easily OOMs.

**P3 — `Mat` leak in the affine alignment branch.** In
`ChannelAligner.EstimateTransform` the success branch returns
`affineHomogeneous.Clone()` without disposing `affine` (the `Mat` from
`Cv2.EstimateAffinePartial2D`). The trailing `affine?.Dispose()` is only reached
on fall-through. Every channel that resolves to an affine transform leaks one
native `Mat`.

## 2. Goals / Non-Goals

### Goals

- Eliminate the GPU-backend leak and per-heal context churn (P1, P1b).
- Eliminate the affine `Mat` leak (P3).
- Bound undo-history memory with a RAM-aware budget while keeping the common-case
  (parameter) edits effectively free (P2).
- No regression in undo/redo behavior or compute diagnostics logging.

### Non-Goals

- No change to alignment math, healing quality, or the reconstruction pipeline.
- No on-disk spill / persistence of undo history (still in-memory only).
- No mask compression or buffer de-duplication in v1 (listed as optional future
  work in §6).
- No change to project save/load format.

## 3. P1 / P1b — Compute backend lifetime

### Design

Separate the **expensive leaf backends** (CUDA, ILGPU, CPU — which own native
contexts) from the **cheap fallback wrapper** (a list + per-call logging).

1. Cache the leaf backends as process-wide singletons, built once and thread-safe:

   ```csharp
   private static readonly Lazy<IReadOnlyList<IImageComputeBackend>> Leaves =
       new(BuildLeaves, LazyThreadSafetyMode.ExecutionAndPublication);

   private static IReadOnlyList<IImageComputeBackend> BuildLeaves()
   {
       List<IImageComputeBackend> leaves = [];
       if (CudaNative.IsAvailable) leaves.Add(new NativeCudaImageComputeBackend());
       if (IlgpuImageComputeBackend.TryCreatePreferred(out var ilgpu)) leaves.Add(ilgpu);
       leaves.Add(new CpuImageComputeBackend());
       return leaves;
   }
   ```

2. `CreateBest(diagnostics)` returns a **cheap** wrapper over the cached leaves,
   so the GPU context is created once but per-call logging is preserved:

   ```csharp
   public static IImageComputeBackend CreateBest(IProcessingDiagnostics? diagnostics = null) =>
       new FallbackImageComputeBackend(Leaves.Value, diagnostics, ownsBackends: false);
   ```

3. `FallbackImageComputeBackend` gains an `ownsBackends` flag. When `false`
   (the shared-leaf case), `Dispose()` does nothing to the leaves. When `true`
   (owned chains, e.g. tests, `CreateCpu`), `Dispose()` disposes each child. This
   fixes P1b without disposing shared singletons.

4. Log backend availability once, when leaves are first built (e.g.
   `"[compute] backends: cuda, ilgpu-cpu, cpu"`), in addition to the existing
   per-call attempt logging that the wrapper already does.

Leaf singletons live for process lifetime (idiomatic for ILGPU `Context`). They
are intentionally never disposed mid-run; the OS reclaims them at exit. This
matches the prior `Lazy` singleton behavior, minus the leak.

### Why this over the alternatives

- `using` at each call site: would not work — `FallbackImageComputeBackend.Dispose`
  is a no-op (P1b), and even fixed it would re-create the ILGPU context per heal
  (slow churn).
- Whole-chain singleton built with `NullProcessingDiagnostics`: kills the leak and
  churn but loses all per-call compute logging. The leaf-cache approach keeps
  logging because the per-call diagnostics flows through the cheap wrapper.

### Call sites

`ChannelHealer.cs:398`, `ChannelHealer.cs:570`, `ChannelRetoucher.cs:85` keep their
current `var backend = ImageComputeBackendFactory.CreateBest(options.Diagnostics);`
call — now cheap and leak-free. No `using` required (wrapper owns nothing). The
trivial wrapper allocation per heal is negligible.

## 4. P3 — Affine `Mat` leak

In `ChannelAligner.EstimateTransform`, scope `affine` with `using` so it is
disposed on every path, and remove the now-redundant fall-through
`affine?.Dispose()`:

```csharp
using var affine = Cv2.EstimateAffinePartial2D(srcInput, dstInput, affineInliers, RobustEstimationAlgorithms.RANSAC, 3.0);
inliers = CountInliers(affineInliers);
if (affine is not null && !affine.Empty() && inliers >= MinimumAffineInliers)
{
    using var affineHomogeneous = ToHomogeneous(affine);
    if (IsWithinTranslationLimit(affineHomogeneous, maxTranslation))
    {
        kind = "affine";
        return affineHomogeneous.Clone(); // caller owns an independent matrix
    }
}
// (no trailing affine?.Dispose(); `using` handles all paths)
```

`Clone()` already returns an independent `Mat`, so disposing `affine` on the
success path is safe.

## 5. P2 — Undo history memory

### Key relationships (verified against current code)

- `Result` is a pure function of `LastAligned` + color/levels/exposure params
  (+ manual nudges): this is exactly what `RebuildResult` /
  `RebuildResultAfterDelay` / `CropOverlap` already compute via
  `ReconstructionPipeline.BuildRgb`.
- `LastAligned` carries masks + metadata + transforms that are **not** reliably
  recoverable from the slot channels (the working channels can diverge from the
  aligned channels after retouch). Therefore `LastAligned` must still be stored
  for snapshot states; it cannot be recomputed in general.
- Parameter commands (`CoalescedParameterCommand`) never touch channels or
  `LastAligned`; they only change scalars.

### Design

Differentiate mementos by what the command changed, drop the one cleanly-derivable
heavy field, and cap total bytes.

**5.1 Memento kinds.** Add a `Kind` discriminator to `EditorMemento`:

- `Parameter` — scalars only. **No `Red`/`Green`/`Blue`/`Result`/`LastAligned`.**
  Used by `CoalescedParameterCommand`.
- `Snapshot` — native-format channel buffers + `LastAligned` + source paths +
  scalars. **`Result` is NOT stored** (recomputed on restore). Used by
  `SnapshotCommand`.

**5.2 Drop `Result` everywhere; recompute on restore.** In `ApplyEditorMemento`:

```text
Result = LastAligned is null ? null : BuildRgb(LastAligned, settings, manualNudges).Rgb
```

For `Parameter` mementos, `LastAligned` is unchanged from the live state, so the
result is rebuilt from the live `lastAligned` + the restored scalars (reuse
`ScheduleResultRebuild` / a synchronous `BuildRgb`). Invariant to confirm in
implementation: there is no reachable state with `Result != null` while
`lastAligned == null` (code review indicates `Result` is always derived from
aligned channels; `ClearAlignedAfterInputEdit` nulls both). If that invariant does
not hold somewhere, fall back to storing `Result` for that specific state.

**5.3 Native pixel format.** Stored channel buffers (snapshot mementos and
`LastAligned`) are kept in their loaded native format (UInt16 / UInt8), never
widened to float.

**5.4 Restore symmetry for redo.** Undo/redo stacks store mementos, and
`PerformUndo`/`PerformRedo` push a freshly captured "current state" onto the
opposite stack. The captured counter-state must use the **same `Kind`** as the
target being restored (peek the target's `Kind` before taking it). So undoing a
`Parameter` memento captures a `Parameter` memento for redo, and undoing a
`Snapshot` captures a `Snapshot`. `EditorHistory` exposes the target `Kind` (or
takes a capture factory keyed by `Kind`) so `MainViewModel` captures the right
shape.

**5.5 Byte-budget eviction.** `EditorHistory` tracks the approximate byte size of
each memento and evicts oldest-first when the running total exceeds a budget,
keeping the existing count cap (20) as a secondary bound:

- Each memento reports `ApproximateBytes` (sum of stored buffer lengths ×
  bytes-per-sample; parameter mementos ≈ 0).
- Budget is **RAM-aware**, not a fixed constant: a fraction of
  `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (or a configured override),
  e.g. default 25%, clamped to a sane floor/ceiling. Sized this way because a
  single 40 MP snapshot can be ~1 GB.
- Eviction removes from the bottom of the undo stack first. If a single new
  snapshot exceeds the entire budget, keep at least one entry (best-effort, never
  evict the entry being pushed).

### Memory impact (40 MP channel, 16-bit)

| Item | Before | After |
|------|--------|-------|
| Parameter edit memento | ~1 GB | ~0 (scalars) |
| Snapshot memento | ~1 GB (R+G+B + Result + LastAligned) | ~0.6 GB (R+G+B + LastAligned; no Result) |
| Total history | unbounded × 20 | bounded by RAM-aware byte budget |

The dominant real-world win is 5.1: slider/parameter history becomes free, which
is the bulk of recorded commands.

## 6. Optional future work (out of scope for v1)

- **Buffer de-duplication.** After align, slot channels and `LastAligned` channels
  are frequently the same buffer instance; storing once and sharing a reference
  would roughly halve snapshot memento size. Requires reference-equality tracking
  in `EditorSession.CreateMemento`.
- **Mask RLE compression.** The 3 overlap masks are largely uniform 0/1 regions
  and compress well.
- **Disk spill** for cold history entries.

## 7. Testing

### Core

- `ChannelAligner`: a test that drives an affine-resolving alignment and exercises
  the success branch (regression guard for P3). Existing alignment tests must
  still pass.
- `ImageComputeBackendFactory`: `CreateBest` returns a usable backend; repeated
  calls reuse the same leaf instances (assert reference equality of leaves, or a
  build-count of 1). `FallbackImageComputeBackend` with `ownsBackends: true`
  disposes children; with `false` does not.

### Gui

- Parameter-undo allocates no image buffers: a `Parameter` memento has null
  channels/result/aligned; undo restores scalars and rebuilds the result.
- Snapshot-undo restores pixels correctly after `Result` recompute (compare
  recomputed result to a reference for a known channel set + params).
- Redo round-trips for both kinds; counter-state kind matches target kind.
- `EditorHistory` byte budget: pushing past the budget evicts oldest-first;
  count cap still applies; a single over-budget snapshot keeps ≥1 entry.

All existing 155 tests must remain green.

## 8. Rollout & versioning

Two stages, landed independently:

- **Stage 1 — leak fixes (PATCH, `0.13.1 → 0.13.2`).** P1, P1b, P3. Small, low
  risk, no user-visible behavior change.
- **Stage 2 — undo redesign (MINOR, `0.13.x → 0.14.0`).** P2. Changes undo memory
  behavior (graceful degradation under pressure) — a backwards-compatible feature
  change per the `0.x` SemVer policy in `AGENTS.md`.

Each stage updates `CHANGELOG.md`. Exact version bump confirmed with the
maintainer at implementation time per the `AGENTS.md` versioning rule.

## 9. Risks & open questions

- **R1 (P2 invariant).** Confirm no reachable state has `Result != null` while
  `lastAligned == null`. Mitigation: per-state fallback to storing `Result`.
- **R2 (recompute latency).** Recomputing `Result` on undo runs `BuildRgb` at full
  resolution (~40 MP). It is paid only on undo/redo, off the slider hot path, and
  the existing rebuild paths already do this. Acceptable; revisit if undo feels
  sluggish on the largest plates.
- **R3 (diagnostics granularity).** The leaf-cache wrapper preserves per-call
  compute logging; only the chain *construction* log moves to first-use. No
  expected loss.
- **R4 (budget tuning).** RAM-aware default (25%) is a starting point; expose an
  override if real-world plates need tuning.
