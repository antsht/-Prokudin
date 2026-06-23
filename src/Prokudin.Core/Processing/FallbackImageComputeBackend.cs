namespace Prokudin.Core.Processing;

internal sealed class FallbackImageComputeBackend(IReadOnlyList<IImageComputeBackend> backends) : IImageComputeBackend
{
    public AccelerationBackendKind Kind => backends.Count > 0 ? backends[0].Kind : AccelerationBackendKind.Cpu;

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
        foreach (var backend in backends)
        {
            if (backend.TryDetectDefectMask(
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
                    outputMask))
            {
                return true;
            }
        }

        return false;
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
        foreach (var backend in backends)
        {
            if (backend.TryPredictMasked(target, guide1, guide2, defectMask, coefficientA, coefficientB, coefficientC, output))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryApplyGain(float[] source, float gain, float[] output)
    {
        foreach (var backend in backends)
        {
            if (backend.TryApplyGain(source, gain, output))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
    }
}
