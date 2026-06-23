namespace Prokudin.Core.Processing;

internal sealed class NativeCudaImageComputeBackend : IImageComputeBackend
{
    public AccelerationBackendKind Kind => AccelerationBackendKind.NativeCuda;

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
        byte[] outputMask) =>
        CudaNative.TryDetectDefectMask(
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

    public bool TryPredictMasked(
        float[] target,
        float[] guide1,
        float[] guide2,
        byte[] defectMask,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float[] output) =>
        CudaNative.TryPredictMasked(
            target,
            guide1,
            guide2,
            defectMask,
            coefficientA,
            coefficientB,
            coefficientC,
            output);

    public bool TryApplyGain(float[] source, float gain, float[] output) => false;

    public void Dispose()
    {
    }
}
