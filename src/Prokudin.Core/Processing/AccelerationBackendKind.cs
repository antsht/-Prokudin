namespace Prokudin.Core.Processing;

public enum AccelerationBackendKind
{
    Cpu,
    NativeCuda,
    IlgpuCuda,
    IlgpuOpenCl,
    IlgpuCpu,
}
