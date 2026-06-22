# Parallel Processing and Optional CUDA Backend Design

Status: approved for implementation planning
Date: 2026-06-22

## Goal

Speed up image processing and filter operations without changing user-visible behavior. The first implementation pass focuses on safe CPU multi-threading and creates an explicit optional CUDA backend point for later kernels.

The application must continue to run on machines without NVIDIA hardware, CUDA Toolkit, or CUDA drivers.

## Architecture

Keep the current static Core API intact. Add a small processing layer inside `Prokudin.Core`:

- `Processing/PixelParallel.cs`: internal helper around `Parallel.For` with a size threshold. Small buffers run sequentially to avoid scheduling overhead.
- `Processing/AccelerationBackendKind.cs`: enum for `Cpu` and `CudaAvailable`.
- `Processing/CudaBackendProbe.cs`: detects CUDA driver availability without a required native dependency. On Windows the first check can use `NativeLibrary.TryLoad("nvcuda.dll")`.

Current operations still execute through CPU paths. CUDA availability must never be required for normal workflows, and CUDA probe failures must fall back to CPU without throwing into image processing commands.

## First Pass Scope

Parallelize operations where each iteration writes to an independent destination index or row:

- `ImageBuffer`: normalized copy and format conversion loops.
- `ImageLoader`: grayscale/RGB row conversion, export image conversion, and RGB resize.
- `ChannelExposure`: per-pixel exposure gain.
- `ImageTransformer`: manual transform output image and mask generation by row.
- `Cropper`: RGB merge, non-overlap grayscale conversion, and selected bounding-box reductions with thread-local state.
- `ColorCorrection`: white balance, temperature/tint, level application, brightness computation, and channel scaling. Percentile sorting remains sequential.
- `ReconstructionPipeline`: RGB resize, 3x3 blur, and unsharp mask.
- `ChannelRetoucher`: mask conversion, high-pass absolute difference, clone stamp pixel pass, and auto-clean raw mask generation using a byte buffer before converting to `Mat`.
- `AvaloniaBitmapFactory`: preview, thumbnail, and mask overlay byte-buffer generation.

OpenCV operations such as `Cv2.Inpaint`, feature matching, warping, morphology, Gaussian blur, and connected-components remain sequential at the call site because they are native calls and may already use OpenCV-level threading.

## Deferred Scope

Do not parallelize `PatchHealer` donor search or `CrossChannelPredictor` loops that directly depend on `Mat.At<T>` and shared OpenCV objects in the first pass. These are phase-2 candidates after masks are snapshotted into managed arrays and donor scoring is isolated from OpenCV interop.

Do not add real `.cu` kernels in this pass. CUDA is represented as a detected optional backend and a documented extension point.

## CUDA Direction

The future CUDA backend should be opt-in at runtime and always keep CPU fallback. First kernel candidates:

- RGB merge and exposure/color transforms.
- RGB resize.
- unsharp mask/box blur.
- mask conversion and simple threshold/diff operations.

The native CUDA library should be loaded dynamically only when present. Missing driver, missing runtime library, unsupported GPU, or kernel launch failure should return to CPU behavior with diagnostic status rather than failing reconstruction or GUI workflows.

## Testing

All existing Core and GUI tests should keep passing with unchanged results.

Add focused tests for:

- `PixelParallel` producing the same output as a sequential loop.
- small workloads using the sequential branch.
- `CudaBackendProbe` returning a backend kind without throwing on machines without CUDA.

No test may require an NVIDIA GPU or CUDA Toolkit.

## Implementation Notes

Use `Parallel.For` for CPU-bound loops and keep writes partitioned by index or row. Avoid shared mutable accumulators unless using thread-local aggregation and a final reduction. Keep cancellation behavior unchanged; existing async workflows still own user-facing cancellation and responsiveness.

Prefer simple row- or index-based parallelism over broad pipeline concurrency. The GUI already moves expensive retouch operations to background tasks; this design speeds the work inside those tasks.
