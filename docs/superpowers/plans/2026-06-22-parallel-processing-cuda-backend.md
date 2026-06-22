# Parallel Processing and Optional CUDA Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe CPU multi-threading for image-processing loops and introduce an optional CUDA availability probe with CPU fallback.

**Architecture:** Keep the existing static Core API intact. Add small processing helpers under `Prokudin.Core.Processing`, then replace independent pixel/row loops with `PixelParallel.For` while leaving OpenCV call sites sequential. CUDA is detected as an optional capability only; no CUDA kernels or required native dependencies are added in this pass.

**Tech Stack:** .NET 10, C# 14, `System.Threading.Tasks.Parallel`, `System.Runtime.InteropServices.NativeLibrary`, xUnit, FluentAssertions, OpenCvSharp, ImageSharp, Avalonia.

---

### Task 1: Baseline and Processing Helpers

**Files:**
- Create: `src/Prokudin.Core/Processing/PixelParallel.cs`
- Create: `src/Prokudin.Core/Processing/AccelerationBackendKind.cs`
- Create: `src/Prokudin.Core/Processing/CudaBackendProbe.cs`
- Create: `tests/Prokudin.Core.Tests/Processing/PixelParallelTests.cs`
- Create: `tests/Prokudin.Core.Tests/Processing/CudaBackendProbeTests.cs`

- [ ] **Step 1: Run baseline tests**

Run: `rtk dotnet test Prokudin.slnx -v:minimal`

Expected: existing tests pass. If restore/build fails because of environment setup, run the same command once with escalation and continue only after the failure is understood.

- [ ] **Step 2: Add focused helper tests**

Add tests that verify `PixelParallel.For` mutates every index exactly once, uses the sequential branch for small workloads via an observable thread id set, and that `CudaBackendProbe.GetBackendKind()` returns a valid enum without throwing.

- [ ] **Step 3: Add helper implementation**

Implement `PixelParallel.For(int fromInclusive, int toExclusive, Action<int> body)` and `PixelParallel.ForRows(int height, Action<int> body)` with a threshold constant and `MaxDegreeOfParallelism = Environment.ProcessorCount`.

Implement `AccelerationBackendKind` with `Cpu` and `CudaAvailable`.

Implement `CudaBackendProbe.GetBackendKind()` using `NativeLibrary.TryLoad("nvcuda.dll", out var handle)` and `NativeLibrary.Free(handle)` on success, returning `Cpu` on all failures.

- [ ] **Step 4: Run helper tests**

Run: `rtk dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~Processing" -v:minimal`

Expected: new helper tests pass.

### Task 2: Parallelize Core Imaging and Pipeline Loops

**Files:**
- Modify: `src/Prokudin.Core/Imaging/ImageBuffer.cs`
- Modify: `src/Prokudin.Core/Imaging/ImageLoader.cs`
- Modify: `src/Prokudin.Core/Color/ChannelExposure.cs`
- Modify: `src/Prokudin.Core/Color/ColorCorrection.cs`
- Modify: `src/Prokudin.Core/Transform/ImageTransformer.cs`
- Modify: `src/Prokudin.Core/Crop/Cropper.cs`
- Modify: `src/Prokudin.Core/Pipeline/ReconstructionPipeline.cs`

- [ ] **Step 1: Add `using Prokudin.Core.Processing` where needed**

Only files that call `PixelParallel` need the using.

- [ ] **Step 2: Replace independent index loops**

Use `PixelParallel.For` for loops where each iteration writes one destination index or pixel triplet:

```csharp
PixelParallel.For(0, result.PixelCount, i =>
{
    result.SetNormalized(i, Math.Clamp(image.GetNormalized(i) * gain, 0.0f, 1.0f));
});
```

- [ ] **Step 3: Replace independent row loops**

Use `PixelParallel.ForRows` for transform, resize, crop-copy, and blur loops where each row writes a disjoint slice.

- [ ] **Step 4: Keep reductions deterministic**

For bounding boxes, use thread-local min/max values and a locked final reduction, or keep the existing sequential scan if the extra complexity would exceed the benefit.

- [ ] **Step 5: Run Core tests**

Run: `rtk dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj -v:minimal`

Expected: Core tests pass with unchanged behavior.

### Task 3: Parallelize Retouch Safe Loops

**Files:**
- Modify: `src/Prokudin.Core/Retouch/ChannelRetoucher.cs`
- Modify: `src/Prokudin.Core/Retouch/ChannelHealer.cs`

- [ ] **Step 1: Parallelize managed-array loops only**

Use `PixelParallel.For` for mask normalization, mask extraction, high-pass absolute difference, feather alpha post-processing, clone stamp pixel application, and auto-clean raw mask byte generation.

- [ ] **Step 2: Avoid parallel OpenCV `Mat` mutation**

For auto-clean detection, compute a `byte[] rawMaskBytes` in parallel, then create a `Mat` from that byte array sequentially through a helper. Do not call `rawMask.Set` from multiple threads.

- [ ] **Step 3: Keep component healing sequential**

Do not parallelize component iteration, `PatchHealer`, or `CrossChannelPredictor` in this pass.

- [ ] **Step 4: Run retouch tests**

Run: `rtk dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter "FullyQualifiedName~Retouch|FullyQualifiedName~Heal|FullyQualifiedName~Channel" -v:minimal`

Expected: retouch-related tests pass.

### Task 4: Parallelize GUI Bitmap Generation

**Files:**
- Modify: `src/Prokudin.Gui/Imaging/AvaloniaBitmapFactory.cs`

- [ ] **Step 1: Use Core processing helper from GUI**

Reference `Prokudin.Core.Processing` and use `PixelParallel.For` or `ForRows` only for filling managed byte arrays.

- [ ] **Step 2: Keep `WriteableBitmap` locking sequential**

Do not access Avalonia bitmap buffers from parallel loops; only the temporary managed byte array is parallelized.

- [ ] **Step 3: Run GUI tests**

Run: `rtk dotnet test tests/Prokudin.Gui.Tests/Prokudin.Gui.Tests.csproj -v:minimal`

Expected: GUI tests pass.

### Task 5: Documentation and Full Verification

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/development.md`

- [ ] **Step 1: Document acceleration behavior**

Add a short section stating that CPU pixel loops use `PixelParallel`, OpenCV calls remain native/sequential at call sites, and CUDA is currently an optional availability probe with CPU fallback.

- [ ] **Step 2: Run full tests**

Run: `rtk dotnet test Prokudin.slnx -v:minimal`

Expected: all tests pass.

- [ ] **Step 3: Run build**

Run: `rtk dotnet build Prokudin.slnx --no-restore -v:minimal`

Expected: build succeeds.

- [ ] **Step 4: Review diff**

Run: `rtk git -c safe.directory='D:/!PROGRAMMING/-Prokudin' diff --stat` and `rtk git -c safe.directory='D:/!PROGRAMMING/-Prokudin' diff --check`.

Expected: changed files match the plan and `diff --check` reports no whitespace errors.
