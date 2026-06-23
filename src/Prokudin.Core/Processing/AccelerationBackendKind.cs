namespace Prokudin.Core.Processing;

public enum AccelerationBackendKind
{
    Cpu,
    NativeCuda,
    CudaAvailable = NativeCuda,
    IlgpuCuda,
    IlgpuOpenCl,
    IlgpuCpu,
}
