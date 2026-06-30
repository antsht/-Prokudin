using System.Threading;
using Prokudin.Core.Diagnostics;

namespace Prokudin.Core.Processing;

internal static class ImageComputeBackendFactory
{
    private static readonly Lazy<IReadOnlyList<IImageComputeBackend>> LeafBackends =
        new(BuildLeaves, LazyThreadSafetyMode.ExecutionAndPublication);

    private static int availabilityLogged;

    public static IImageComputeBackend CreateBest(IProcessingDiagnostics? diagnostics = null)
    {
        var leaves = LeafBackends.Value;
        LogAvailabilityOnce(leaves, diagnostics ?? NullProcessingDiagnostics.Instance);
        return new FallbackImageComputeBackend(leaves, diagnostics, ownsBackends: false);
    }

    public static IImageComputeBackend CreateCpu() => new CpuImageComputeBackend();

    public static bool TryCreateIlgpuCpu(out IImageComputeBackend backend)
    {
        if (IlgpuImageComputeBackend.TryCreateCpu(out var ilgpu))
        {
            backend = ilgpu;
            return true;
        }

        backend = null!;
        return false;
    }

    private static IReadOnlyList<IImageComputeBackend> BuildLeaves()
    {
        List<IImageComputeBackend> backends = [];

        if (CudaNative.IsAvailable)
        {
            backends.Add(new NativeCudaImageComputeBackend());
        }

        if (IlgpuImageComputeBackend.TryCreatePreferred(out var ilgpu))
        {
            backends.Add(ilgpu);
        }

        backends.Add(new CpuImageComputeBackend());
        return backends;
    }

    private static void LogAvailabilityOnce(IReadOnlyList<IImageComputeBackend> backends, IProcessingDiagnostics diagnostics)
    {
        if (Interlocked.Exchange(ref availabilityLogged, 1) != 0)
        {
            return;
        }

        diagnostics.Log(
            ProcessingLogCategory.ComputeBackend,
            $"[compute] backends: {string.Join(", ", backends.Select(backend => backend.Kind))}");
    }
}
