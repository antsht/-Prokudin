using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;

namespace Prokudin.Core.Processing;

internal sealed class IlgpuImageComputeBackend : IImageComputeBackend
{
    private readonly Context context;
    private readonly Accelerator accelerator;

    private readonly Action<
        Index1D,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        DefectMaskParameters,
        ArrayView1D<byte, Stride1D.Dense>> detectDefectMaskKernel;

    private readonly Action<
        Index1D,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<float, Stride1D.Dense>,
        ArrayView1D<byte, Stride1D.Dense>,
        PredictionParameters,
        ArrayView1D<float, Stride1D.Dense>> predictMaskedKernel;

    private readonly Action<
        Index1D,
        ArrayView1D<float, Stride1D.Dense>,
        float,
        ArrayView1D<float, Stride1D.Dense>> applyGainKernel;

    private IlgpuImageComputeBackend(Context context, Accelerator accelerator, AccelerationBackendKind kind)
    {
        this.context = context;
        this.accelerator = accelerator;
        Kind = kind;
        detectDefectMaskKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            DefectMaskParameters,
            ArrayView1D<byte, Stride1D.Dense>>(DetectDefectMaskKernel);
        predictMaskedKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<float, Stride1D.Dense>,
            ArrayView1D<byte, Stride1D.Dense>,
            PredictionParameters,
            ArrayView1D<float, Stride1D.Dense>>(PredictMaskedKernel);
        applyGainKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<float, Stride1D.Dense>,
            float,
            ArrayView1D<float, Stride1D.Dense>>(ApplyGainKernel);
    }

    public AccelerationBackendKind Kind { get; }

    public static bool TryCreatePreferred(out IlgpuImageComputeBackend backend)
    {
        backend = null!;
        try
        {
            var context = Context.CreateDefault();
            var device = context.GetPreferredDevice(preferCPU: false);
            var accelerator = device.CreateAccelerator(context);
            backend = new IlgpuImageComputeBackend(context, accelerator, ToBackendKind(accelerator.AcceleratorType));
            return backend.Kind is AccelerationBackendKind.IlgpuCuda or AccelerationBackendKind.IlgpuOpenCl;
        }
        catch
        {
            backend?.Dispose();
            backend = null!;
            return false;
        }
    }

    public static bool TryCreateCpu(out IlgpuImageComputeBackend backend)
    {
        backend = null!;
        try
        {
            var context = Context.CreateDefault();
            var accelerator = context.CreateCPUAccelerator(0);
            backend = new IlgpuImageComputeBackend(context, accelerator, AccelerationBackendKind.IlgpuCpu);
            return true;
        }
        catch
        {
            backend?.Dispose();
            backend = null!;
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
        if (!ValidateSameLength(
                target,
                other1,
                other2,
                targetHighPass,
                other1HighPass,
                other2HighPass,
                outputMask))
        {
            return false;
        }

        try
        {
            using var targetBuffer = accelerator.Allocate1D(target);
            using var other1Buffer = accelerator.Allocate1D(other1);
            using var other2Buffer = accelerator.Allocate1D(other2);
            using var targetHighPassBuffer = accelerator.Allocate1D(targetHighPass);
            using var other1HighPassBuffer = accelerator.Allocate1D(other1HighPass);
            using var other2HighPassBuffer = accelerator.Allocate1D(other2HighPass);
            using var outputBuffer = accelerator.Allocate1D<byte>(outputMask.Length);
            detectDefectMaskKernel(
                target.Length,
                targetBuffer.View,
                other1Buffer.View,
                other2Buffer.View,
                targetHighPassBuffer.View,
                other1HighPassBuffer.View,
                other2HighPassBuffer.View,
                new DefectMaskParameters(
                    (float)coefficientA,
                    (float)coefficientB,
                    (float)coefficientC,
                    residualThreshold,
                    highPassThreshold,
                    supportMultiplier,
                    supportOffset),
                outputBuffer.View);
            accelerator.Synchronize();
            outputBuffer.CopyToCPU(outputMask);
            return true;
        }
        catch
        {
            return false;
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
        if (!ValidateSameLength(target, guide1, guide2, defectMask, output))
        {
            return false;
        }

        try
        {
            using var targetBuffer = accelerator.Allocate1D(target);
            using var guide1Buffer = accelerator.Allocate1D(guide1);
            using var guide2Buffer = accelerator.Allocate1D(guide2);
            using var maskBuffer = accelerator.Allocate1D(defectMask);
            using var outputBuffer = accelerator.Allocate1D<float>(output.Length);
            predictMaskedKernel(
                output.Length,
                targetBuffer.View,
                guide1Buffer.View,
                guide2Buffer.View,
                maskBuffer.View,
                new PredictionParameters((float)coefficientA, (float)coefficientB, (float)coefficientC),
                outputBuffer.View);
            accelerator.Synchronize();
            outputBuffer.CopyToCPU(output);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryApplyGain(float[] source, float gain, float[] output)
    {
        if (!ValidateSameLength(source, output))
        {
            return false;
        }

        try
        {
            using var sourceBuffer = accelerator.Allocate1D(source);
            using var outputBuffer = accelerator.Allocate1D<float>(output.Length);
            applyGainKernel(output.Length, sourceBuffer.View, gain, outputBuffer.View);
            accelerator.Synchronize();
            outputBuffer.CopyToCPU(output);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryHighPassAbs(float[] source, int width, int height, double sigma, float[] output) => false;

    public void Dispose()
    {
        accelerator.Dispose();
        context.Dispose();
    }

    private static void DetectDefectMaskKernel(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> target,
        ArrayView1D<float, Stride1D.Dense> other1,
        ArrayView1D<float, Stride1D.Dense> other2,
        ArrayView1D<float, Stride1D.Dense> targetHighPass,
        ArrayView1D<float, Stride1D.Dense> other1HighPass,
        ArrayView1D<float, Stride1D.Dense> other2HighPass,
        DefectMaskParameters parameters,
        ArrayView1D<byte, Stride1D.Dense> outputMask)
    {
        var i = index.X;
        var prediction = Clamp01((parameters.CoefficientA * other1[i]) + (parameters.CoefficientB * other2[i]) + parameters.CoefficientC);
        var residual = Abs(target[i] - prediction);
        var otherSupport = Max(other1HighPass[i], other2HighPass[i]);
        outputMask[i] = residual > parameters.ResidualThreshold &&
                        targetHighPass[i] > parameters.HighPassThreshold &&
                        targetHighPass[i] > (otherSupport * parameters.SupportMultiplier) + parameters.SupportOffset
            ? (byte)1
            : (byte)0;
    }

    private static void PredictMaskedKernel(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> target,
        ArrayView1D<float, Stride1D.Dense> guide1,
        ArrayView1D<float, Stride1D.Dense> guide2,
        ArrayView1D<byte, Stride1D.Dense> defectMask,
        PredictionParameters parameters,
        ArrayView1D<float, Stride1D.Dense> output)
    {
        var i = index.X;
        output[i] = defectMask[i] > 0
            ? Clamp01((parameters.CoefficientA * guide1[i]) + (parameters.CoefficientB * guide2[i]) + parameters.CoefficientC)
            : target[i];
    }

    private static void ApplyGainKernel(
        Index1D index,
        ArrayView1D<float, Stride1D.Dense> source,
        float gain,
        ArrayView1D<float, Stride1D.Dense> output)
    {
        var i = index.X;
        output[i] = Clamp01(source[i] * gain);
    }

    private static bool ValidateSameLength(Array first, params Array[] rest) =>
        first.Length > 0 && rest.All(buffer => buffer.Length == first.Length);

    private static AccelerationBackendKind ToBackendKind(AcceleratorType acceleratorType) =>
        acceleratorType switch
        {
            AcceleratorType.Cuda => AccelerationBackendKind.IlgpuCuda,
            AcceleratorType.OpenCL => AccelerationBackendKind.IlgpuOpenCl,
            AcceleratorType.CPU => AccelerationBackendKind.IlgpuCpu,
            _ => AccelerationBackendKind.IlgpuCpu,
        };

    private static float Clamp01(float value) =>
        value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value;

    private static float Abs(float value) => value < 0.0f ? -value : value;

    private static float Max(float left, float right) => left > right ? left : right;

    private readonly record struct DefectMaskParameters(
        float CoefficientA,
        float CoefficientB,
        float CoefficientC,
        float ResidualThreshold,
        float HighPassThreshold,
        float SupportMultiplier,
        float SupportOffset);

    private readonly record struct PredictionParameters(float CoefficientA, float CoefficientB, float CoefficientC);
}
