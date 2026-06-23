using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

internal readonly record struct PredictionResult(
    float[] Prediction,
    float Confidence,
    bool Succeeded,
    LinearModel Model);

internal static class CrossChannelPredictor
{
    public static PredictionResult PredictComponent(
        ImageBuffer target,
        ImageBuffer guide1,
        ImageBuffer guide2,
        Mat componentMask,
        Mat globalDefectMask,
        HealOptions options)
    {
        var prediction = new float[target.PixelCount];
        return PredictComponent(target, guide1, guide2, componentMask, globalDefectMask, options, prediction);
    }

    public static PredictionResult PredictComponent(
        ImageBuffer target,
        ImageBuffer guide1,
        ImageBuffer guide2,
        Mat componentMask,
        Mat globalDefectMask,
        HealOptions options,
        float[] prediction)
    {
        var width = target.Width;
        var height = target.Height;
        var pixelCount = width * height;
        if (prediction.Length < pixelCount)
        {
            throw new ArgumentException("Prediction buffer is too small.", nameof(prediction));
        }

        target.CopyNormalizedTo(prediction);

        var contextRadius = options.NormalizedContextRadius;
        using var ring = HealingMaskUtils.BuildRingMask(componentMask, contextRadius, globalDefectMask);
        var trainingCount = HealingMaskUtils.CountNonZero(ring);
        if (trainingCount < options.MinTrainingPixels && contextRadius < 32)
        {
            ring.Dispose();
            using var expandedRing = HealingMaskUtils.BuildRingMask(componentMask, 32, globalDefectMask);
            trainingCount = HealingMaskUtils.CountNonZero(expandedRing);
            if (trainingCount < options.MinTrainingPixels)
            {
                return new PredictionResult(prediction, 0.0f, false, new LinearModel(0, 0, 0, 0));
            }

            return PredictWithRing(target, guide1, guide2, componentMask, expandedRing, options, prediction);
        }

        if (trainingCount < options.MinTrainingPixels)
        {
            return new PredictionResult(prediction, 0.0f, false, new LinearModel(0, 0, 0, 0));
        }

        return PredictWithRing(target, guide1, guide2, componentMask, ring, options, prediction);
    }

    private static PredictionResult PredictWithRing(
        ImageBuffer target,
        ImageBuffer guide1,
        ImageBuffer guide2,
        Mat componentMask,
        Mat ringMask,
        HealOptions options,
        float[] prediction)
    {
        var width = target.Width;
        var height = target.Height;
        var targetValues = new List<float>();
        var guide1Values = new List<float>();
        var guide2Values = new List<float>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (ringMask.At<byte>(y, x) == 0)
                {
                    continue;
                }

                var index = (y * width) + x;
                targetValues.Add(target.GetNormalized(index));
                guide1Values.Add(guide1.GetNormalized(index));
                guide2Values.Add(guide2.GetNormalized(index));
            }
        }

        if (targetValues.Count < options.MinTrainingPixels)
        {
            return new PredictionResult(prediction, 0.0f, false, new LinearModel(0, 0, 0, 0));
        }

        var model = LinearModelFitter.Fit(targetValues, guide1Values, guide2Values, options.UseRobustFit);
        if (model.Count == 0)
        {
            return new PredictionResult(prediction, 0.0f, false, model);
        }

        var errorSum = 0.0f;
        for (var i = 0; i < targetValues.Count; i++)
        {
            var predicted = LinearModelFitter.Predict(model, guide1Values[i], guide2Values[i]);
            errorSum += Math.Abs(targetValues[i] - predicted);
        }

        var meanAbsError = errorSum / targetValues.Count;
        var maxAllowedError = target.MaxAllowedHealError;
        var confidence = 1.0f - Math.Clamp(meanAbsError / maxAllowedError, 0.0f, 1.0f);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (componentMask.At<byte>(y, x) == 0)
                {
                    continue;
                }

                var index = (y * width) + x;
                prediction[index] = Math.Clamp(
                    LinearModelFitter.Predict(model, guide1.GetNormalized(index), guide2.GetNormalized(index)),
                    0.0f,
                    1.0f);
            }
        }

        return new PredictionResult(prediction, confidence, true, model);
    }
}
