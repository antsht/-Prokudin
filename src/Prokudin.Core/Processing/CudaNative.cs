using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Prokudin.Core.Processing;

internal static class CudaNative
{
    private const string DisableEnvironmentVariable = "PROKUDIN_DISABLE_CUDA";
    private const string DllPathEnvironmentVariable = "PROKUDIN_CUDA_DLL";
    private const string WindowsLibraryName = "Prokudin.Cuda.dll";
    private const string LinuxLibraryName = "libProkudin.Cuda.so";
    private const int PredictMaskedBlockPixels = 8 * 1024 * 1024;

    private static readonly Lazy<NativeApi?> Api = new(LoadApi);

    public static bool IsAvailable => Api.Value?.IsAvailable() ?? false;

    public static bool TryDetectDefectMask(
        float[] target,
        float[] other1,
        float[] other2,
        float[] targetHighPass,
        float[] other1HighPass,
        float[] other2HighPass,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float residualThreshold,
        float highPassThreshold,
        float supportMultiplier,
        float supportOffset,
        byte[] outputMask)
    {
        var api = Api.Value;
        if (api is null ||
            target.Length == 0 ||
            other1.Length != target.Length ||
            other2.Length != target.Length ||
            targetHighPass.Length != target.Length ||
            other1HighPass.Length != target.Length ||
            other2HighPass.Length != target.Length ||
            outputMask.Length != target.Length)
        {
            return false;
        }

        return api.TryDetectDefectMask(
            target,
            other1,
            other2,
            targetHighPass,
            other1HighPass,
            other2HighPass,
            coefficientA,
            coefficientB,
            coefficientC,
            residualThreshold,
            highPassThreshold,
            supportMultiplier,
            supportOffset,
            outputMask);
    }

    public static bool TryPredictMasked(
        float[] target,
        float[] guide1,
        float[] guide2,
        byte[] defectMask,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float[] output)
    {
        var api = Api.Value;
        if (api is null ||
            target.Length == 0 ||
            guide1.Length != target.Length ||
            guide2.Length != target.Length ||
            defectMask.Length != target.Length ||
            output.Length != target.Length)
        {
            return false;
        }

        return api.TryPredictMasked(
            target,
            guide1,
            guide2,
            defectMask,
            coefficientA,
            coefficientB,
            coefficientC,
            output);
    }

    private static NativeApi? LoadApi()
    {
        if (IsDisabled())
        {
            return null;
        }

        foreach (var candidate in CandidateLibraryPaths())
        {
            if (!NativeLibrary.TryLoad(candidate, out var handle))
            {
                continue;
            }

            if (NativeApi.TryCreate(handle, out var api))
            {
                return api;
            }

            NativeLibrary.Free(handle);
        }

        return null;
    }

    private static bool IsDisabled()
    {
        var value = Environment.GetEnvironmentVariable(DisableEnvironmentVariable);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> CandidateLibraryPaths()
    {
        var configured = Environment.GetEnvironmentVariable(DllPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var libraryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? WindowsLibraryName
            : LinuxLibraryName;

        foreach (var root in WalkUp(AppContext.BaseDirectory))
        {
            yield return Path.Combine(root, libraryName);
            yield return Path.Combine(root, "native", "Prokudin.Cuda", "bin", libraryName);
        }

        foreach (var root in WalkUp(Environment.CurrentDirectory))
        {
            yield return Path.Combine(root, libraryName);
            yield return Path.Combine(root, "native", "Prokudin.Cuda", "bin", libraryName);
        }

        yield return libraryName;
    }

    private static IEnumerable<string> WalkUp(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IsAvailableDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DetectDefectMaskDelegate(
        IntPtr target,
        IntPtr other1,
        IntPtr other2,
        IntPtr targetHighPass,
        IntPtr other1HighPass,
        IntPtr other2HighPass,
        int length,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float residualThreshold,
        float highPassThreshold,
        float supportMultiplier,
        float supportOffset,
        IntPtr outputMask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PredictMaskedDelegate(
        IntPtr target,
        IntPtr guide1,
        IntPtr guide2,
        IntPtr defectMask,
        int length,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        IntPtr output);

    private sealed class NativeApi
    {
        private readonly IntPtr handle;
        private readonly IsAvailableDelegate isAvailable;
        private readonly DetectDefectMaskDelegate detectDefectMask;
        private readonly PredictMaskedDelegate predictMasked;

        private NativeApi(
            IntPtr handle,
            IsAvailableDelegate isAvailable,
            DetectDefectMaskDelegate detectDefectMask,
            PredictMaskedDelegate predictMasked)
        {
            this.handle = handle;
            this.isAvailable = isAvailable;
            this.detectDefectMask = detectDefectMask;
            this.predictMasked = predictMasked;
        }

        ~NativeApi()
        {
            NativeLibrary.Free(handle);
        }

        public static bool TryCreate(IntPtr handle, [NotNullWhen(true)] out NativeApi? api)
        {
            api = null;
            if (!NativeLibrary.TryGetExport(handle, "ProkudinCudaIsAvailable", out var isAvailablePointer) ||
                !NativeLibrary.TryGetExport(handle, "ProkudinCudaDetectDefectMask", out var detectDefectMaskPointer) ||
                !NativeLibrary.TryGetExport(handle, "ProkudinCudaPredictMasked", out var predictMaskedPointer))
            {
                return false;
            }

            api = new NativeApi(
                handle,
                Marshal.GetDelegateForFunctionPointer<IsAvailableDelegate>(isAvailablePointer),
                Marshal.GetDelegateForFunctionPointer<DetectDefectMaskDelegate>(detectDefectMaskPointer),
                Marshal.GetDelegateForFunctionPointer<PredictMaskedDelegate>(predictMaskedPointer));
            return true;
        }

        public bool IsAvailable()
        {
            try
            {
                return isAvailable() == 1;
            }
            catch
            {
                return false;
            }
        }

        public bool TryDetectDefectMask(
            float[] target,
            float[] other1,
            float[] other2,
            float[] targetHighPass,
            float[] other1HighPass,
            float[] other2HighPass,
            double coefficientA,
            double coefficientB,
            double coefficientC,
            float residualThreshold,
            float highPassThreshold,
            float supportMultiplier,
            float supportOffset,
            byte[] outputMask)
        {
            if (!IsAvailable())
            {
                return false;
            }

            var handles = new[]
            {
                GCHandle.Alloc(target, GCHandleType.Pinned),
                GCHandle.Alloc(other1, GCHandleType.Pinned),
                GCHandle.Alloc(other2, GCHandleType.Pinned),
                GCHandle.Alloc(targetHighPass, GCHandleType.Pinned),
                GCHandle.Alloc(other1HighPass, GCHandleType.Pinned),
                GCHandle.Alloc(other2HighPass, GCHandleType.Pinned),
                GCHandle.Alloc(outputMask, GCHandleType.Pinned),
            };

            try
            {
                var status = detectDefectMask(
                    handles[0].AddrOfPinnedObject(),
                    handles[1].AddrOfPinnedObject(),
                    handles[2].AddrOfPinnedObject(),
                    handles[3].AddrOfPinnedObject(),
                    handles[4].AddrOfPinnedObject(),
                    handles[5].AddrOfPinnedObject(),
                    target.Length,
                    coefficientA,
                    coefficientB,
                    coefficientC,
                    residualThreshold,
                    highPassThreshold,
                    supportMultiplier,
                    supportOffset,
                    handles[6].AddrOfPinnedObject());
                return status == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (var pinnedHandle in handles)
                {
                    pinnedHandle.Free();
                }
            }
        }

        public bool TryPredictMasked(
            float[] target,
            float[] guide1,
            float[] guide2,
            byte[] defectMask,
            double coefficientA,
            double coefficientB,
            double coefficientC,
            float[] output)
        {
            if (!IsAvailable())
            {
                return false;
            }

            var handles = new[]
            {
                GCHandle.Alloc(target, GCHandleType.Pinned),
                GCHandle.Alloc(guide1, GCHandleType.Pinned),
                GCHandle.Alloc(guide2, GCHandleType.Pinned),
                GCHandle.Alloc(defectMask, GCHandleType.Pinned),
                GCHandle.Alloc(output, GCHandleType.Pinned),
            };

            try
            {
                var targetPointer = handles[0].AddrOfPinnedObject();
                var guide1Pointer = handles[1].AddrOfPinnedObject();
                var guide2Pointer = handles[2].AddrOfPinnedObject();
                var maskPointer = handles[3].AddrOfPinnedObject();
                var outputPointer = handles[4].AddrOfPinnedObject();

                for (var offset = 0; offset < target.Length; offset += PredictMaskedBlockPixels)
                {
                    var blockLength = Math.Min(PredictMaskedBlockPixels, target.Length - offset);
                    var floatByteOffset = offset * sizeof(float);
                    var status = predictMasked(
                        IntPtr.Add(targetPointer, floatByteOffset),
                        IntPtr.Add(guide1Pointer, floatByteOffset),
                        IntPtr.Add(guide2Pointer, floatByteOffset),
                        IntPtr.Add(maskPointer, offset),
                        blockLength,
                        coefficientA,
                        coefficientB,
                        coefficientC,
                        IntPtr.Add(outputPointer, floatByteOffset));
                    if (status != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (var pinnedHandle in handles)
                {
                    pinnedHandle.Free();
                }
            }
        }
    }
}
