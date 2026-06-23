namespace Prokudin.Core.Processing;

public static class CudaBackendProbe
{
    public static AccelerationBackendKind GetBackendKind()
    {
        return ImageComputeBackendFactory.CreateBest().Kind;
    }
}
