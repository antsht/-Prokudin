namespace Prokudin.Core.Processing;

public static class CudaBackendProbe
{
    public static AccelerationBackendKind GetBackendKind()
    {
        return CudaNative.IsAvailable
            ? AccelerationBackendKind.CudaAvailable
            : AccelerationBackendKind.Cpu;
    }
}
