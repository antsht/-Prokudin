# Processing Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add switchable processing diagnostics (compute backends, pipeline stages, CPU parallel, timings) to the GUI log with persisted settings and zero overhead when disabled.

**Architecture:** Introduce `IProcessingDiagnostics` in `Prokudin.Core.Diagnostics` with `NullProcessingDiagnostics` as default. Instrument `FallbackImageComputeBackend`, `PixelParallel`, alignment, reconstruction, retouch, and exposure at subsystem boundaries. GUI wires toggles to a filtering sink and JSON settings store.

**Tech Stack:** .NET 10, C# 14, Avalonia, CommunityToolkit.Mvvm, xUnit, FluentAssertions, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-06-23-processing-diagnostics-design.md`

---

## File map

| File | Responsibility |
| --- | --- |
| `Diagnostics/ProcessingLogCategory.cs` | A/B/C flags enum |
| `Diagnostics/ProcessingDiagnosticsOptions.cs` | Enabled categories + timings flag |
| `Diagnostics/IProcessingDiagnostics.cs` | Sink contract |
| `Diagnostics/NullProcessingDiagnostics.cs` | No-op singleton |
| `Diagnostics/FilteringProcessingDiagnostics.cs` | Category/timing filter wrapper |
| `Diagnostics/ScopedProcessingDiagnostics.cs` | Scope + parallel aggregation on dispose |
| `Diagnostics/CapturingProcessingDiagnostics.cs` | Test capture list |
| `Diagnostics/ProcessingDiagnosticsAmbient.cs` | `AsyncLocal` active scope for `PixelParallel` |
| `Processing/FallbackImageComputeBackend.cs` | Log each backend attempt (A) |
| `Processing/PixelParallel.cs` | Record parallel mode when scope active (C) |
| `Alignment/ChannelAligner.cs` | Alignment stage logs (B, C) |
| `Pipeline/ReconstructionPipeline.cs` | Rebuild/align scopes (B) |
| `Color/ChannelExposure.cs` | ApplyGain backend + scope (A, B) |
| `Retouch/ChannelRetoucher.cs` | Auto-clean detect scopes (A, B) |
| `Retouch/ChannelHealer.cs` | Healing path logs (B) |
| `Gui/Diagnostics/GuiProcessingDiagnostics.cs` | Forward to `AppendLog` |
| `Gui/Services/JsonProcessingDiagnosticsSettingsStore.cs` | Persist toggles |
| `MainViewModel.cs` + `MainWindow.axaml` | UI toggles and wiring |

---

### Task 1: Core diagnostics types and tests

**Files:**
- Create: `src/Prokudin.Core/Diagnostics/ProcessingLogCategory.cs`
- Create: `src/Prokudin.Core/Diagnostics/ProcessingDiagnosticsOptions.cs`
- Create: `src/Prokudin.Core/Diagnostics/IProcessingDiagnostics.cs`
- Create: `src/Prokudin.Core/Diagnostics/NullProcessingDiagnostics.cs`
- Create: `src/Prokudin.Core/Diagnostics/FilteringProcessingDiagnostics.cs`
- Create: `src/Prokudin.Core/Diagnostics/CapturingProcessingDiagnostics.cs`
- Create: `src/Prokudin.Core/Diagnostics/ScopedProcessingDiagnostics.cs`
- Create: `src/Prokudin.Core/Diagnostics/ProcessingDiagnosticsAmbient.cs`
- Create: `tests/Prokudin.Core.Tests/Diagnostics/ProcessingDiagnosticsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Diagnostics;

public sealed class ProcessingDiagnosticsTests
{
    [Fact]
    public void NullDiagnostics_DoesNotThrow()
    {
        var diagnostics = NullProcessingDiagnostics.Instance;
        using var scope = diagnostics.BeginScope("test", ProcessingLogCategory.PipelineStage);
        diagnostics.Log(ProcessingLogCategory.ComputeBackend, "ignored");
        diagnostics.LogComputeAttempt("ApplyGain", AccelerationBackendKind.Cpu, succeeded: true);
    }

    [Fact]
    public void FilteringDiagnostics_RespectsCategoryFlags()
    {
        var capture = new CapturingProcessingDiagnostics();
        var diagnostics = new FilteringProcessingDiagnostics(
            capture,
            new ProcessingDiagnosticsOptions(ProcessingLogCategory.ComputeBackend, IncludeTimings: false));

        diagnostics.Log(ProcessingLogCategory.PipelineStage, "hidden");
        diagnostics.Log(ProcessingLogCategory.ComputeBackend, "visible");

        capture.Lines.Should().ContainSingle().Which.Should().Be("visible");
    }

    [Fact]
    public void ScopedDiagnostics_EmitsParallelSummaryOnDispose()
    {
        var capture = new CapturingProcessingDiagnostics();
        var options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.CpuParallel, IncludeTimings: false);
        var root = new FilteringProcessingDiagnostics(capture, options);

        using (root.BeginScope("BuildRgb.merge", ProcessingLogCategory.CpuParallel))
        {
            ProcessingDiagnosticsAmbient.RecordParallel("ForRows", iterationCount: 8192, usedParallel: true, maxDegree: 16);
        }

        capture.Lines.Should().ContainSingle(line => line.Contains("BuildRgb.merge") && line.Contains("Parallel"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~ProcessingDiagnosticsTests" -v:minimal`

Expected: FAIL — types not found.

- [ ] **Step 3: Implement diagnostics types**

`ProcessingLogCategory.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

[Flags]
public enum ProcessingLogCategory
{
    None = 0,
    ComputeBackend = 1,
    PipelineStage = 2,
    CpuParallel = 4,
    All = ComputeBackend | PipelineStage | CpuParallel,
}
```

`ProcessingDiagnosticsOptions.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

public sealed record ProcessingDiagnosticsOptions(
    ProcessingLogCategory EnabledCategories = ProcessingLogCategory.None,
    bool IncludeTimings = false)
{
    public bool IsEnabled(ProcessingLogCategory category) =>
        category != ProcessingLogCategory.None && (EnabledCategories & category) == category;
}
```

`IProcessingDiagnostics.cs`:

```csharp
using Prokudin.Core.Processing;

namespace Prokudin.Core.Diagnostics;

public interface IProcessingDiagnostics
{
    ProcessingDiagnosticsOptions Options { get; }
    IDisposable BeginScope(string operationName, ProcessingLogCategory category);
    void Log(ProcessingLogCategory category, string message);
    void LogComputeAttempt(
        string operation,
        AccelerationBackendKind backend,
        bool succeeded,
        long? elapsedMs = null,
        string? failureReason = null);
}
```

`NullProcessingDiagnostics.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

public sealed class NullProcessingDiagnostics : IProcessingDiagnostics
{
    public static NullProcessingDiagnostics Instance { get; } = new();
    private NullProcessingDiagnostics() { }
    public ProcessingDiagnosticsOptions Options { get; } = new();
    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) => Disposable.Empty;
    public void Log(ProcessingLogCategory category, string message) { }
    public void LogComputeAttempt(string operation, Processing.AccelerationBackendKind backend, bool succeeded, long? elapsedMs = null, string? failureReason = null) { }
}
```

`CapturingProcessingDiagnostics.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

public sealed class CapturingProcessingDiagnostics : IProcessingDiagnostics
{
    public List<string> Lines { get; } = [];
    public ProcessingDiagnosticsOptions Options { get; set; } = new(ProcessingLogCategory.All, IncludeTimings: true);
    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
        new ScopedProcessingDiagnostics(this, operationName, category);
    public void Log(ProcessingLogCategory category, string message) => Lines.Add(message);
    public void LogComputeAttempt(string operation, Processing.AccelerationBackendKind backend, bool succeeded, long? elapsedMs = null, string? failureReason = null) =>
        Lines.Add($"{operation}:{backend}:{(succeeded ? "ok" : "fail")}");
}
```

`FilteringProcessingDiagnostics.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

public sealed class FilteringProcessingDiagnostics(IProcessingDiagnostics inner, ProcessingDiagnosticsOptions options) : IProcessingDiagnostics
{
    public ProcessingDiagnosticsOptions Options { get; } = options;
    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
        options.IsEnabled(category)
            ? new ScopedProcessingDiagnostics(this, operationName, category)
            : Disposable.Empty;
    public void Log(ProcessingLogCategory category, string message)
    {
        if (options.IsEnabled(category))
        {
            inner.Log(category, message);
        }
    }
    public void LogComputeAttempt(string operation, Processing.AccelerationBackendKind backend, bool succeeded, long? elapsedMs = null, string? failureReason = null)
    {
        if (options.IsEnabled(ProcessingLogCategory.ComputeBackend))
        {
            inner.LogComputeAttempt(operation, backend, succeeded, options.IncludeTimings ? elapsedMs : null, failureReason);
        }
    }
}
```

`ProcessingDiagnosticsAmbient.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

public static class ProcessingDiagnosticsAmbient
{
    private static readonly AsyncLocal<ScopedProcessingDiagnostics?> ActiveScope = new();

    internal static void Push(ScopedProcessingDiagnostics scope) => ActiveScope.Value = scope;
    internal static void Pop(ScopedProcessingDiagnostics scope)
    {
        if (ReferenceEquals(ActiveScope.Value, scope))
        {
            ActiveScope.Value = null;
        }
    }

    public static void RecordParallel(string method, long iterationCount, bool usedParallel, int maxDegree) =>
        ActiveScope.Value?.RecordParallel(method, iterationCount, usedParallel, maxDegree);
}
```

`ScopedProcessingDiagnostics.cs`:

```csharp
namespace Prokudin.Core.Diagnostics;

public sealed class ScopedProcessingDiagnostics : IDisposable
{
    private readonly IProcessingDiagnostics diagnostics;
    private readonly string operationName;
    private readonly ProcessingLogCategory category;
    private string? parallelSummary;

    public ScopedProcessingDiagnostics(IProcessingDiagnostics diagnostics, string operationName, ProcessingLogCategory category)
    {
        this.diagnostics = diagnostics;
        this.operationName = operationName;
        this.category = category;
        ProcessingDiagnosticsAmbient.Push(this);
    }

    internal void RecordParallel(string method, long iterationCount, bool usedParallel, int maxDegree)
    {
        parallelSummary = usedParallel
            ? $"{method} {iterationCount} iter, MDP={maxDegree}"
            : $"{method} {iterationCount} iter, sequential";
    }

    public void Dispose()
    {
        ProcessingDiagnosticsAmbient.Pop(this);
        if (parallelSummary is not null)
        {
            diagnostics.Log(ProcessingLogCategory.CpuParallel, $"[parallel] {operationName}: {parallelSummary}");
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~ProcessingDiagnosticsTests" -v:minimal`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Diagnostics tests/Prokudin.Core.Tests/Diagnostics
git commit -m "feat: add processing diagnostics core types"
```

---

### Task 2: Instrument FallbackImageComputeBackend (category A)

**Files:**
- Modify: `src/Prokudin.Core/Processing/FallbackImageComputeBackend.cs`
- Modify: `src/Prokudin.Core/Processing/ImageComputeBackendFactory.cs` (optional: pass diagnostics via factory later; v1 pass `IProcessingDiagnostics?` into backend constructor)
- Create: `tests/Prokudin.Core.Tests/Diagnostics/FallbackBackendDiagnosticsTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public void FallbackBackend_LogsEachAttempt_WhenDiagnosticsEnabled()
{
    var capture = new CapturingProcessingDiagnostics
    {
        Options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.ComputeBackend, IncludeTimings: true),
    };
    var cpu = new CpuImageComputeBackend();
    var backend = new FallbackImageComputeBackend([cpu], capture);

    var pixels = new float[64];
    var mask = new byte[64];
    backend.TryDetectDefectMask(pixels, pixels, pixels, pixels, pixels, pixels, 1, 0, 0, 0.1f, 0.1f, 1f, 0f, mask)
        .Should().BeTrue();

    capture.Lines.Should().Contain(line => line.Contains("Cpu") && line.Contains("ok"));
}
```

- [ ] **Step 2: Run test — expect FAIL**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~FallbackBackendDiagnosticsTests" -v:minimal`

- [ ] **Step 3: Add diagnostics to FallbackImageComputeBackend**

Add constructor overload or optional parameter:

```csharp
internal sealed class FallbackImageComputeBackend(
    IReadOnlyList<IImageComputeBackend> backends,
    IProcessingDiagnostics? diagnostics = null) : IImageComputeBackend
{
    private readonly IProcessingDiagnostics diagnostics = diagnostics ?? NullProcessingDiagnostics.Instance;
```

Wrap each `Try*` attempt with `Stopwatch` when `Options.IncludeTimings`, call `LogComputeAttempt` before return. Log operation preamble once with pixel count derived from `target.Length`.

Update `ImageComputeBackendFactory.CreateBestCore()` to use `new FallbackImageComputeBackend(backends)` — diagnostics injected per call site in Task 4–6, not at singleton factory.

**Important:** `CreateBest()` returns a shared singleton without diagnostics. Call sites that need logging should either:
- call `new FallbackImageComputeBackend([...], diagnostics)` locally, or
- add `ImageComputeBackendFactory.CreateBest(IProcessingDiagnostics? diagnostics)` overload used by instrumented paths.

Prefer **per-call factory overload** to avoid breaking singleton probe behavior:

```csharp
public static IImageComputeBackend CreateBest(IProcessingDiagnostics? diagnostics = null)
{
    if (diagnostics is null or NullProcessingDiagnostics)
    {
        return BestBackend.Value;
    }

    return CreateChain(diagnostics);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~FallbackBackendDiagnostics|FullyQualifiedName~ImageComputeBackend" -v:minimal`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Processing tests/Prokudin.Core.Tests/Diagnostics
git commit -m "feat: log compute backend fallback attempts"
```

---

### Task 3: Instrument PixelParallel (category C)

**Files:**
- Modify: `src/Prokudin.Core/Processing/PixelParallel.cs`

- [ ] **Step 1: Extend PixelParallel methods**

After choosing sequential vs parallel branch, call:

```csharp
ProcessingDiagnosticsAmbient.RecordParallel(
    method: "For",
    iterationCount: toExclusive - fromInclusive,
    usedParallel: toExclusive - fromInclusive >= MinimumParallelIterations && Environment.ProcessorCount > 1,
    maxDegree: Options.MaxDegreeOfParallelism);
```

Add `using Prokudin.Core.Diagnostics;` at top. Same for `ForRows` and `Invoke` (use `actions.Length` as iteration count for Invoke).

- [ ] **Step 2: Run existing PixelParallel tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~PixelParallelTests" -v:minimal`

Expected: PASS (ambient is inert without active scope)

- [ ] **Step 3: Commit**

```bash
git add src/Prokudin.Core/Processing/PixelParallel.cs
git commit -m "feat: record PixelParallel mode for diagnostics scopes"
```

---

### Task 4: Thread diagnostics through settings records

**Files:**
- Modify: `src/Prokudin.Core/Pipeline/PipelineSettings.cs`
- Modify: `src/Prokudin.Core/Retouch/HealOptions.cs`
- Modify: `src/Prokudin.Core/Retouch/AutoCleanSettings.cs`

- [ ] **Step 1: Add optional Diagnostics property**

```csharp
// PipelineSettings.cs
using Prokudin.Core.Diagnostics;

public IProcessingDiagnostics? Diagnostics { get; init; }
```

Same pattern on `HealOptions` and `AutoCleanSettings` as optional init property with default `null`.

- [ ] **Step 2: Run full Core tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj -v:minimal`

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Prokudin.Core/Pipeline/PipelineSettings.cs src/Prokudin.Core/Retouch/HealOptions.cs src/Prokudin.Core/Retouch/AutoCleanSettings.cs
git commit -m "feat: add diagnostics hook to pipeline and retouch settings"
```

---

### Task 5: Instrument ChannelExposure and ChannelRetoucher

**Files:**
- Modify: `src/Prokudin.Core/Color/ChannelExposure.cs`
- Modify: `src/Prokudin.Core/Retouch/ChannelRetoucher.cs`
- Create: `tests/Prokudin.Core.Tests/Diagnostics/ChannelExposureDiagnosticsTests.cs`

- [ ] **Step 1: ChannelExposure**

Add optional `IProcessingDiagnostics? diagnostics = null` to `Apply`:

```csharp
public static ImageBuffer Apply(ImageBuffer image, float stops, IProcessingDiagnostics? diagnostics = null)
{
    diagnostics ??= NullProcessingDiagnostics.Instance;
    using var scope = diagnostics.BeginScope($"exposure.{image.Width}x{image.Height}", ProcessingLogCategory.PipelineStage);
    // existing logic; replace CreateBest() with CreateBest(diagnostics)
    // on inline CPU fallback: diagnostics.Log(ComputeBackend, "... CPU inline")
}
```

- [ ] **Step 2: ChannelRetoucher.DetectSingleChannelDefects**

Resolve diagnostics from a new optional parameter or from extended `AutoCleanSettings.Diagnostics`:

```csharp
var diagnostics = settings.Diagnostics ?? NullProcessingDiagnostics.Instance;
using var scope = diagnostics.BeginScope($"auto-clean.detect.{target.Width}x{target.Height}", ProcessingLogCategory.PipelineStage);
// use ImageComputeBackendFactory.CreateBest(diagnostics)
// on BuildRawMask CPU fallback: Log compute inline message
```

- [ ] **Step 3: Write and run exposure diagnostics test**

```csharp
[Fact]
public void Apply_LogsInlineCpu_WhenStopsNonZeroAndNoGpu()
{
    var capture = new CapturingProcessingDiagnostics
    {
        Options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.All, IncludeTimings: false),
    };
    using var image = TestImageBuffer.SolidGray(64, 64, 0.5f);
    ChannelExposure.Apply(image, 1.0f, capture);
    capture.Lines.Should().NotBeEmpty();
}
```

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~ChannelExposureDiagnostics" -v:minimal`

- [ ] **Step 4: Commit**

```bash
git add src/Prokudin.Core/Color/ChannelExposure.cs src/Prokudin.Core/Retouch/ChannelRetoucher.cs tests
git commit -m "feat: diagnostics for exposure and auto-clean detect"
```

---

### Task 6: Instrument ChannelAligner (category B + C)

**Files:**
- Modify: `src/Prokudin.Core/Alignment/ChannelAligner.cs`
- Modify: `src/Prokudin.Core/Pipeline/ReconstructionPipeline.cs`
- Create: `tests/Prokudin.Core.Tests/Diagnostics/ChannelAlignerDiagnosticsTests.cs`

- [ ] **Step 1: Add optional diagnostics parameter to public AlignChannel**

```csharp
public static AlignResult AlignChannel(
    ImageBuffer reference,
    ImageBuffer moving,
    AlignOptions? options = null,
    IProcessingDiagnostics? diagnostics = null)
```

Pass into private `AlignChannel` overload.

- [ ] **Step 2: Log alignment events**

Examples to add when `PipelineStage` enabled:

```csharp
diagnostics.Log(ProcessingLogCategory.PipelineStage,
    $"[align] featureless identity (σ_ref={stdRef:E1}, σ_mov={stdMov:E1})");

diagnostics.Log(ProcessingLogCategory.PipelineStage,
    $"[align] coarse {detector}@{coarseSide} scale={searchScale:F3}, {matchCount} matches → {kind} {inliers} inliers");
```

When `CpuParallel` enabled at start of align:

```csharp
diagnostics.Log(ProcessingLogCategory.CpuParallel, $"[parallel] OpenCV threads={Cv2.GetNumThreads()}");
```

- [ ] **Step 3: ReconstructionPipeline passes diagnostics**

```csharp
var diagnostics = settings.Diagnostics ?? NullProcessingDiagnostics.Instance;
using var alignScope = diagnostics.BeginScope("RunAutoAlign", ProcessingLogCategory.PipelineStage);
// per channel:
var result = ChannelAligner.AlignChannel(reference, channels[name], settings.Align, diagnostics);
```

`BuildRgb`:

```csharp
using var scope = diagnostics.BeginScope("BuildRgb", ProcessingLogCategory.PipelineStage);
var red = ChannelExposure.Apply(aligned.Red, settings.Exposure.RedStops, diagnostics);
```

- [ ] **Step 4: Featureless align test**

Use uniform gray buffers; assert capture contains `identity` and not `coarse`.

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Alignment/ChannelAligner.cs src/Prokudin.Core/Pipeline/ReconstructionPipeline.cs tests
git commit -m "feat: diagnostics for alignment and reconstruction pipeline"
```

---

### Task 7: Instrument ChannelHealer (category B)

**Files:**
- Modify: `src/Prokudin.Core/Retouch/ChannelHealer.cs`

- [ ] **Step 1: Resolve diagnostics from HealOptions**

```csharp
var diagnostics = options.Diagnostics ?? NullProcessingDiagnostics.Instance;
using var scope = diagnostics.BeginScope("HealChannel", ProcessingLogCategory.PipelineStage);
```

- [ ] **Step 2: Log healing path branches**

At branch entry (Telea, patch, cross-channel, large bulk):

```csharp
diagnostics.Log(ProcessingLogCategory.PipelineStage,
    $"[retouch] large bulk path: {components.Count} components, {defectPixelCount} px");
```

Keep existing `StatusMessage` for GUI Normal log; diagnostics adds detail when B enabled.

- [ ] **Step 3: Use CreateBest(diagnostics) in large-mask prediction**

- [ ] **Step 4: Run heal tests**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~Heal" -v:minimal`

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Retouch/ChannelHealer.cs
git commit -m "feat: diagnostics for channel healing paths"
```

---

### Task 8: GUI settings store and sink

**Files:**
- Create: `src/Prokudin.Gui/Services/ProcessingDiagnosticsSettings.cs`
- Create: `src/Prokudin.Gui/Services/IProcessingDiagnosticsSettingsStore.cs`
- Create: `src/Prokudin.Gui/Services/JsonProcessingDiagnosticsSettingsStore.cs`
- Create: `src/Prokudin.Gui/Diagnostics/GuiProcessingDiagnostics.cs`
- Create: `tests/Prokudin.Gui.Tests/ProcessingDiagnosticsSettingsStoreTests.cs`

- [ ] **Step 1: Settings record**

```csharp
namespace Prokudin.Gui.Services;

public sealed record ProcessingDiagnosticsSettings(
    bool LogComputeBackends = false,
    bool LogPipelineStages = false,
    bool LogCpuParallel = false,
    bool LogTimings = false)
{
    public static ProcessingDiagnosticsSettings Default { get; } = new();

    public ProcessingDiagnosticsOptions ToOptions()
    {
        var categories = ProcessingLogCategory.None;
        if (LogComputeBackends) categories |= ProcessingLogCategory.ComputeBackend;
        if (LogPipelineStages) categories |= ProcessingLogCategory.PipelineStage;
        if (LogCpuParallel) categories |= ProcessingLogCategory.CpuParallel;
        return new ProcessingDiagnosticsOptions(categories, LogTimings);
    }
}
```

- [ ] **Step 2: JSON store at `%LocalAppData%/Prokudin/diagnostics-settings.json`**

Mirror `JsonExportSettingsStore` error handling.

- [ ] **Step 3: GuiProcessingDiagnostics**

```csharp
public sealed class GuiProcessingDiagnostics(Action<string> appendLog, ProcessingDiagnosticsOptions options)
    : FilteringProcessingDiagnostics(new ForwardingDiagnostics(appendLog), options);

file sealed class ForwardingDiagnostics(Action<string> appendLog) : IProcessingDiagnostics
{
    public ProcessingDiagnosticsOptions Options { get; } = new(ProcessingLogCategory.All, true);
    public IDisposable BeginScope(string operationName, ProcessingLogCategory category) =>
        new ScopedProcessingDiagnostics(this, operationName, category);
    public void Log(ProcessingLogCategory category, string message) => appendLog(message);
    public void LogComputeAttempt(string operation, AccelerationBackendKind backend, bool succeeded, long? elapsedMs = null, string? failureReason = null)
    {
        var timing = elapsedMs is null ? string.Empty : $" [{elapsedMs}ms]";
        var status = succeeded ? "ok" : $"fail ({failureReason ?? "unknown"})";
        appendLog($"[compute] {operation}: {backend} {status}{timing}");
    }
}
```

- [ ] **Step 4: Store round-trip test**

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Gui/Services src/Prokudin.Gui/Diagnostics tests/Prokudin.Gui.Tests
git commit -m "feat: GUI processing diagnostics settings and sink"
```

---

### Task 9: MainViewModel wiring and UI toggles

**Files:**
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`
- Modify: `src/Prokudin.Gui/Views/MainWindow.axaml`
- Modify: `tests/Prokudin.Gui.Tests/MainViewModelTests.cs` (optional toggle test)

- [ ] **Step 1: Add store dependency**

Constructor injection with default `new JsonProcessingDiagnosticsSettingsStore()`, same pattern as export settings.

- [ ] **Step 2: Observable properties**

```csharp
[ObservableProperty]
private bool logComputeBackends;

[ObservableProperty]
private bool logPipelineStages;

[ObservableProperty]
private bool logCpuParallel;

[ObservableProperty]
private bool logTimings;

partial void OnLogComputeBackendsChanged(bool value) => SaveDiagnosticsSettings();
// same for other three
```

- [ ] **Step 3: CreateDiagnostics helper**

```csharp
private IProcessingDiagnostics CreateDiagnostics() =>
    new GuiProcessingDiagnostics(
        message => AppendLog(message),
        CurrentDiagnosticsSettings().ToOptions());

private PipelineSettings CurrentPipelineSettings(bool skipCrop = false) =>
    new()
    {
        // existing fields...
        Diagnostics = CreateDiagnostics(),
    };
```

Pass `Diagnostics = CreateDiagnostics()` into `CreateHealOptions()` and `CreateAutoCleanSettings()`.

- [ ] **Step 4: Add checkboxes in MainWindow.axaml above log panel**

```xml
<StackPanel Orientation="Horizontal" Spacing="8" Margin="0,0,0,4">
  <CheckBox Content="Backends" IsChecked="{Binding LogComputeBackends}" />
  <CheckBox Content="Pipeline" IsChecked="{Binding LogPipelineStages}" />
  <CheckBox Content="CPU parallel" IsChecked="{Binding LogCpuParallel}" />
  <CheckBox Content="Timings" IsChecked="{Binding LogTimings}" />
</StackPanel>
```

- [ ] **Step 5: Load settings in constructor after export settings**

- [ ] **Step 6: Run GUI tests**

Run: `dotnet test tests/Prokudin.Gui.Tests/Prokudin.Gui.Tests.csproj -v:minimal`

- [ ] **Step 7: Commit**

```bash
git add src/Prokudin.Gui/ViewModels/MainViewModel.cs src/Prokudin.Gui/Views/MainWindow.axaml tests
git commit -m "feat: GUI toggles for processing diagnostics"
```

---

### Task 10: Documentation and full verification

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/development.md`

- [ ] **Step 1: Add short section to architecture.md**

Document `IProcessingDiagnostics`, three categories, default off, GUI toggles persisted to LocalAppData.

- [ ] **Step 2: Add debugging note to development.md**

How to enable Backends/Pipeline/CPU parallel/Timings in GUI; example log lines.

- [ ] **Step 3: Run full solution tests**

Run: `dotnet test Prokudin.slnx -v:minimal`

Expected: all tests PASS

- [ ] **Step 4: Manual smoke test**

1. Launch GUI
2. Enable all four toggles
3. Run Auto-align on a triptych
4. Verify log contains `[align]`, `[compute]`, and optionally `[parallel]` lines
5. Restart app — toggles restored

- [ ] **Step 5: Commit**

```bash
git add docs/architecture.md docs/development.md
git commit -m "docs: processing diagnostics toggles and log categories"
```

---

## Spec coverage checklist

| Spec requirement | Task |
| --- | --- |
| Category A compute fallback logging | Task 2, 5, 7 |
| Category B pipeline stages | Task 5, 6, 7 |
| Category C PixelParallel + OpenCV threads | Task 3, 6 |
| Timings toggle | Task 1, 2, 8 |
| GUI four checkboxes | Task 9 |
| Persisted settings | Task 8, 9 |
| Null default / no test breakage | Task 1, 4 |
| Noise control / scopes | Task 1, 3, 6 |
| Out of scope CLI | Not included |

## Execution handoff

**Plan complete and saved to `docs/superpowers/plans/2026-06-23-processing-diagnostics.md`.**

**Two execution options:**

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between tasks
2. **Inline Execution** — implement task-by-task in this session with checkpoints

Which approach do you want?
