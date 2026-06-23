using Prokudin.Core.Diagnostics;
using Prokudin.Core.Imaging;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Color;

public static class ChannelExposure
{
    private const int MinimumAcceleratedPixels = PixelParallel.MinimumParallelIterations;

    public static ImageBuffer Apply(ImageBuffer image, float stops, IProcessingDiagnostics? diagnostics = null)
    {
        diagnostics ??= NullProcessingDiagnostics.Instance;
        using var scope = diagnostics.BeginScope(
            $"exposure.{image.Width}x{image.Height}",
            ProcessingLogCategory.PipelineStage);

        if (Math.Abs(stops) < 0.001f)
        {
            return image;
        }

        diagnostics.Log(
            ProcessingLogCategory.PipelineStage,
            $"[pipeline] exposure {stops:+#0.0;-#0.0;0} stops ({image.Width}x{image.Height})");

        var gain = MathF.Pow(2.0f, stops);
        if (image.PixelCount < MinimumAcceleratedPixels)
        {
            diagnostics.Log(
                ProcessingLogCategory.ComputeBackend,
                "[compute] ApplyGain: below accelerated threshold → CPU inline");
            return ApplyCpu(image, gain);
        }

        var source = new float[image.PixelCount];
        var output = new float[image.PixelCount];
        image.CopyNormalizedTo(source);
        if (ImageComputeBackendFactory.CreateBest(diagnostics).TryApplyGain(source, gain, output))
        {
            return ImageBuffer.FromNormalized(image.Width, image.Height, output, image.Format);
        }

        diagnostics.Log(
            ProcessingLogCategory.ComputeBackend,
            "[compute] ApplyGain: all backends failed → CPU inline");
        return ApplyCpu(image, gain, source);
    }

    private static ImageBuffer ApplyCpu(ImageBuffer image, float gain, float[]? normalizedSource = null)
    {
        var source = normalizedSource;
        var result = image.Clone();
        PixelParallel.For(0, result.PixelCount, i =>
        {
            var value = source is null ? image.GetNormalized(i) : source[i];
            result.SetNormalized(i, Math.Clamp(value * gain, 0.0f, 1.0f));
        });

        return result;
    }
}
