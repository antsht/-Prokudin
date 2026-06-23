namespace Prokudin.Core.Processing;

internal sealed class CpuImageComputeBackend : IImageComputeBackend
{
    public AccelerationBackendKind Kind => AccelerationBackendKind.Cpu;

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

        var a = (float)coefficientA;
        var b = (float)coefficientB;
        var c = (float)coefficientC;
        PixelParallel.For(0, target.Length, i =>
        {
            var prediction = Clamp01((a * other1[i]) + (b * other2[i]) + c);
            var residual = Math.Abs(target[i] - prediction);
            var otherSupport = Math.Max(other1HighPass[i], other2HighPass[i]);
            outputMask[i] = residual > residualThreshold &&
                            targetHighPass[i] > highPassThreshold &&
                            targetHighPass[i] > (otherSupport * supportMultiplier) + supportOffset
                ? (byte)1
                : (byte)0;
        });

        return true;
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

        var a = (float)coefficientA;
        var b = (float)coefficientB;
        var c = (float)coefficientC;
        PixelParallel.For(0, output.Length, i =>
        {
            output[i] = defectMask[i] > 0
                ? Clamp01((a * guide1[i]) + (b * guide2[i]) + c)
                : target[i];
        });

        return true;
    }

    public bool TryApplyGain(float[] source, float gain, float[] output)
    {
        if (!ValidateSameLength(source, output))
        {
            return false;
        }

        PixelParallel.For(0, output.Length, i =>
        {
            output[i] = Clamp01(source[i] * gain);
        });

        return true;
    }

    public void Dispose()
    {
    }

    private static bool ValidateSameLength(Array first, params Array[] rest) =>
        first.Length > 0 && rest.All(buffer => buffer.Length == first.Length);

    private static float Clamp01(float value) =>
        value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value;
}
