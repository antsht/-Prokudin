using System.Diagnostics;
using Prokudin.Core.Diagnostics;

namespace Prokudin.Core.Processing;

internal sealed class FallbackImageComputeBackend : IImageComputeBackend
{
    private readonly IReadOnlyList<IImageComputeBackend> backends;
    private readonly IProcessingDiagnostics diagnostics;
    private readonly bool ownsBackends;
    private bool disposed;

    public FallbackImageComputeBackend(
        IReadOnlyList<IImageComputeBackend> backends,
        IProcessingDiagnostics? diagnostics = null,
        bool ownsBackends = true)
    {
        this.backends = backends;
        this.diagnostics = diagnostics ?? NullProcessingDiagnostics.Instance;
        this.ownsBackends = ownsBackends;
    }

    internal IReadOnlyList<IImageComputeBackend> Backends => backends;

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
        byte[] outputMask) =>
        TryWithLogging(
            "DetectDefectMask",
            target.Length,
            backend => backend.TryDetectDefectMask(
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
                outputMask));

    public bool TryPredictMasked(
        float[] target,
        float[] guide1,
        float[] guide2,
        byte[] defectMask,
        double coefficientA,
        double coefficientB,
        double coefficientC,
        float[] output) =>
        TryWithLogging(
            "PredictMasked",
            target.Length,
            backend => backend.TryPredictMasked(
                target,
                guide1,
                guide2,
                defectMask,
                coefficientA,
                coefficientB,
                coefficientC,
                output));

    public bool TryApplyGain(float[] source, float gain, float[] output) =>
        TryWithLogging(
            "ApplyGain",
            source.Length,
            backend => backend.TryApplyGain(source, gain, output));

    public bool TryHighPassAbs(float[] source, int width, int height, double sigma, float[] output) =>
        TryWithLogging(
            "HighPassAbs",
            source.Length,
            backend => backend.TryHighPassAbs(source, width, height, sigma, output));

    public void Dispose()
    {
        if (disposed || !ownsBackends)
        {
            return;
        }

        disposed = true;
        foreach (var backend in backends)
        {
            backend.Dispose();
        }
    }

    private bool TryWithLogging(string operation, int pixelCount, Func<IImageComputeBackend, bool> attempt)
    {
        diagnostics.Log(
            ProcessingLogCategory.ComputeBackend,
            $"[compute] {operation} ({pixelCount:N0} px)");

        foreach (var backend in backends)
        {
            Stopwatch? stopwatch = diagnostics.Options.IncludeTimings ? Stopwatch.StartNew() : null;
            var succeeded = attempt(backend);
            stopwatch?.Stop();
            diagnostics.LogComputeAttempt(
                operation,
                backend.Kind,
                succeeded,
                stopwatch?.ElapsedMilliseconds);

            if (succeeded)
            {
                return true;
            }
        }

        return false;
    }
}
