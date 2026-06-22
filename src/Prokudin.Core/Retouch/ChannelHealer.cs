using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

public static class ChannelHealer
{
    public static HealResult HealChannel(
        ImageBuffer targetChannel,
        ImageBuffer? guideChannel1,
        ImageBuffer? guideChannel2,
        byte[] defectMask,
        HealOptions options)
    {
        ValidateMask(defectMask, targetChannel);
        if (!defectMask.Any(value => value > 0))
        {
            return new HealResult(targetChannel.Clone(), defectMask);
        }

        if (options.Mode == HealingMode.CrossChannelGuided)
        {
            if (guideChannel1 is null || guideChannel2 is null)
            {
                return HealCurrentChannelOnly(
                    targetChannel,
                    defectMask,
                    options with { Mode = HealingMode.CurrentChannelOnly, SubMode = HealingSubMode.Telea },
                    usedFallback: true,
                    statusMessage: "Cross-channel unavailable, using Telea.");
            }

            ValidateSameDimensions(targetChannel, guideChannel1, nameof(guideChannel1));
            ValidateSameDimensions(targetChannel, guideChannel2, nameof(guideChannel2));
            return HealCrossChannel(targetChannel, guideChannel1, guideChannel2, defectMask, options);
        }

        return HealCurrentChannelOnly(targetChannel, defectMask, options);
    }

    private static HealResult HealCurrentChannelOnly(
        ImageBuffer targetChannel,
        byte[] defectMask,
        HealOptions options,
        bool usedFallback = false,
        string? statusMessage = null)
    {
        return options.SubMode switch
        {
            HealingSubMode.Telea => HealTelea(targetChannel, defectMask, options, usedFallback, statusMessage),
            HealingSubMode.Patch => HealPatchOnly(targetChannel, defectMask, options, usedFallback, statusMessage),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.SubMode, "Unsupported healing sub-mode."),
        };
    }

    private static HealResult HealTelea(
        ImageBuffer targetChannel,
        byte[] defectMask,
        HealOptions options,
        bool usedFallback,
        string? statusMessage)
    {
        var healed = ChannelRetoucher.InpaintMask(targetChannel, defectMask, options.NormalizedInpaintRadius);
        return new HealResult(healed, defectMask, UsedFallback: usedFallback, StatusMessage: statusMessage);
    }

    private static HealResult HealPatchOnly(
        ImageBuffer targetChannel,
        byte[] defectMask,
        HealOptions options,
        bool usedFallback,
        string? statusMessage,
        ImageBuffer? guide1 = null,
        ImageBuffer? guide2 = null,
        bool guided = false)
    {
        var (result, averageConfidence) = ApplyPatchHealing(
            targetChannel,
            guide1,
            guide2,
            defectMask,
            options,
            guided);
        return new HealResult(
            result,
            defectMask,
            averageConfidence,
            UsedCrossChannel: guided,
            UsedFallback: usedFallback,
            StatusMessage: statusMessage);
    }

    private static HealResult HealCrossChannel(
        ImageBuffer targetChannel,
        ImageBuffer guide1,
        ImageBuffer guide2,
        byte[] defectMask,
        HealOptions options)
    {
        var result = targetChannel.Clone();
        var width = targetChannel.Width;
        var height = targetChannel.Height;
        using var globalDefectMask = HealingMaskUtils.MaskToMat(defectMask, width, height);
        var components = HealingMaskUtils.FindComponents(defectMask, width, height);
        var usedFallback = false;
        var confidenceSum = 0.0f;
        var confidenceCount = 0;
        var work = new float[width * height];

        try
        {
            foreach (var component in components)
            {
                var area = HealingMaskUtils.CountNonZero(component);
                var isLarge = area > options.MaxComponentArea;

                var prediction = options.UseLocalLinearPrediction
                    ? CrossChannelPredictor.PredictComponent(targetChannel, guide1, guide2, component, globalDefectMask, options)
                    : new PredictionResult(work, 0.0f, false, default);

                var patch = options.UseGuidedPatchSearch
                    ? PatchHealer.HealComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, guided: true)
                    : CreateIdentityPatch(targetChannel);

                if (!prediction.Succeeded && !patch.Succeeded)
                {
                    usedFallback = true;
                    ApplyTeleaComponent(result, targetChannel, component, options, width, height);
                    continue;
                }

                targetChannel.CopyNormalizedTo(work);
                var alpha = prediction.Succeeded
                    ? predictionAlpha(prediction.Confidence, options, isLarge)
                    : 0.0f;

                if (prediction.Confidence < options.LowConfidenceThreshold)
                {
                    alpha = options.PredictionAlphaMin;
                    usedFallback = true;
                }

                if (!prediction.Succeeded)
                {
                    usedFallback = true;
                }

                if (!patch.Succeeded)
                {
                    usedFallback = true;
                    patch = PatchHealer.HealComponent(targetChannel, null, null, component, globalDefectMask, options, guided: false);
                }

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        if (component.At<byte>(y, x) == 0)
                        {
                            continue;
                        }

                        var index = (y * width) + x;
                        var predicted = prediction.Succeeded ? prediction.Prediction[index] : work[index];
                        var patched = patch.Succeeded ? patch.PatchValues[index] : work[index];
                        work[index] = (alpha * predicted) + ((1.0f - alpha) * patched);
                    }
                }

                if (prediction.Succeeded)
                {
                    confidenceSum += prediction.Confidence;
                    confidenceCount++;
                }

                ApplyComponentValues(result, component, work, options.FeatherSigma, width, height);

                if (options.DebugOutput)
                {
                    HealingDebugWriter.SaveComponentDebug(
                        options,
                        targetChannel,
                        component,
                        prediction.Prediction,
                        patch.PatchValues,
                        work,
                        prediction.Confidence);
                }
            }
        }
        finally
        {
            foreach (var component in components)
            {
                component.Dispose();
            }
        }

        if (options.DebugOutput)
        {
            HealingDebugWriter.SaveFinalDebug(options, targetChannel, result, defectMask, width, height);
        }

        var averageConfidence = confidenceCount > 0 ? confidenceSum / confidenceCount : 0.0f;
        return new HealResult(result, defectMask, averageConfidence, UsedCrossChannel: true, UsedFallback: usedFallback);
    }

    private static (ImageBuffer Image, float AverageConfidence) ApplyPatchHealing(
        ImageBuffer targetChannel,
        ImageBuffer? guide1,
        ImageBuffer? guide2,
        byte[] defectMask,
        HealOptions options,
        bool guided)
    {
        var result = targetChannel.Clone();
        var width = targetChannel.Width;
        var height = targetChannel.Height;
        using var globalDefectMask = HealingMaskUtils.MaskToMat(defectMask, width, height);
        var components = HealingMaskUtils.FindComponents(defectMask, width, height);
        var confidenceSum = 0.0f;
        var confidenceCount = 0;

        try
        {
            foreach (var component in components)
            {
                var patch = PatchHealer.HealComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, guided);
                if (!patch.Succeeded)
                {
                    ApplyTeleaComponent(result, targetChannel, component, options, width, height);
                    continue;
                }

                confidenceSum += patch.Confidence;
                confidenceCount++;
                ApplyComponentValues(result, component, patch.PatchValues, options.FeatherSigma, width, height);
            }
        }
        finally
        {
            foreach (var component in components)
            {
                component.Dispose();
            }
        }

        return (result, confidenceCount > 0 ? confidenceSum / confidenceCount : 0.0f);
    }

    private static PatchHealResult CreateIdentityPatch(ImageBuffer targetChannel)
    {
        var values = new float[targetChannel.PixelCount];
        targetChannel.CopyNormalizedTo(values);
        return new PatchHealResult(values, 0.0f, false, default);
    }

    private static float predictionAlpha(float confidence, HealOptions options, bool isLarge)
    {
        var alpha = Math.Clamp(confidence, options.PredictionAlphaMin, options.PredictionAlphaMax);
        return isLarge ? alpha * options.LargeComponentConservativeScale : alpha;
    }

    private static void ApplyTeleaComponent(
        ImageBuffer result,
        ImageBuffer source,
        Mat componentMask,
        HealOptions options,
        int width,
        int height)
    {
        var componentMaskBytes = ExtractComponentMask(componentMask, width, height);
        var telea = ChannelRetoucher.InpaintMask(source, componentMaskBytes, options.NormalizedInpaintRadius);
        var values = new float[width * height];
        telea.CopyNormalizedTo(values);
        ApplyComponentValues(result, componentMask, values, options.FeatherSigma, width, height);
    }

    private static void ApplyComponentValues(
        ImageBuffer result,
        Mat componentMask,
        float[] values,
        float featherSigma,
        int width,
        int height)
    {
        using var softMask = HealingMaskUtils.BuildSoftMask(componentMask, featherSigma);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var maskValue = softMask.At<float>(y, x);
                if (maskValue <= 0.0f)
                {
                    continue;
                }

                var index = (y * width) + x;
                var original = result.GetNormalized(index);
                var blended = (original * (1.0f - maskValue)) + (values[index] * maskValue);
                result.SetNormalized(index, blended);
            }
        }
    }

    private static byte[] ExtractComponentMask(Mat componentMask, int width, int height)
    {
        var mask = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                mask[(y * width) + x] = componentMask.At<byte>(y, x) > 0 ? (byte)1 : (byte)0;
            }
        }

        return mask;
    }

    private static void ValidateMask(byte[] defectMask, ImageBuffer targetChannel)
    {
        if (defectMask.Length != targetChannel.PixelCount)
        {
            throw new ArgumentException("Mask dimensions must match the image.", nameof(defectMask));
        }
    }

    private static void ValidateSameDimensions(ImageBuffer target, ImageBuffer other, string parameterName)
    {
        if (target.Width != other.Width || target.Height != other.Height)
        {
            throw new ArgumentException("All channels must have the same dimensions.", parameterName);
        }
    }
}
