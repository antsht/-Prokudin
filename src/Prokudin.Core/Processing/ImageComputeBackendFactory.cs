using Prokudin.Core.Diagnostics;

namespace Prokudin.Core.Processing;

internal static class ImageComputeBackendFactory
{
    private static readonly Lazy<IImageComputeBackend> BestBackend = new(() => CreateChain(NullProcessingDiagnostics.Instance));

    public static IImageComputeBackend CreateBest(IProcessingDiagnostics? diagnostics = null)
    {
        if (diagnostics is null or NullProcessingDiagnostics)
        {
            return BestBackend.Value;
        }

        return CreateChain(diagnostics);
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

    private static IImageComputeBackend CreateChain(IProcessingDiagnostics diagnostics)
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
        return new FallbackImageComputeBackend(backends, diagnostics);
    }
}
