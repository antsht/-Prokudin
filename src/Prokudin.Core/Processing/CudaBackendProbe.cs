using System.Runtime.InteropServices;

namespace Prokudin.Core.Processing;

public static class CudaBackendProbe
{
    public static AccelerationBackendKind GetBackendKind()
    {
        return TryLoadCudaDriver()
            ? AccelerationBackendKind.CudaAvailable
            : AccelerationBackendKind.Cpu;
    }

    private static bool TryLoadCudaDriver()
    {
        try
        {
            if (!NativeLibrary.TryLoad("nvcuda.dll", out var handle))
            {
                return false;
            }

            NativeLibrary.Free(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
