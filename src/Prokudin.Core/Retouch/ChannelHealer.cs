using OpenCvSharp;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Imaging;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Retouch;

public static class ChannelHealer
{
    public static HealResult HealChannel(
        ImageBuffer targetChannel,
        ImageBuffer? guideChannel1,
        ImageBuffer? guideChannel2,
        byte[] defectMask,
        HealOptions options,
        IProgress<double>? progress = null)
    {
        ReportProgress(progress, 0);
        var diagnostics = options.Diagnostics ?? NullProcessingDiagnostics.Instance;
        using var scope = diagnostics.BeginScope("HealChannel", ProcessingLogCategory.PipelineStage);
        ValidateMask(defectMask, targetChannel);
        if (!defectMask.Any(value => value > 0))
        {
            ReportProgress(progress, 100);
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
                    statusMessage: "Cross-channel unavailable, using Telea.",
                    progress: progress);
            }

            ValidateSameDimensions(targetChannel, guideChannel1, nameof(guideChannel1));
            ValidateSameDimensions(targetChannel, guideChannel2, nameof(guideChannel2));
            return HealCrossChannel(targetChannel, guideChannel1, guideChannel2, defectMask, options, progress);
        }

        return HealCurrentChannelOnly(targetChannel, defectMask, options, progress: progress);
    }

    private static HealResult HealCurrentChannelOnly(
        ImageBuffer targetChannel,
        byte[] defectMask,
        HealOptions options,
        bool usedFallback = false,
        string? statusMessage = null,
        IProgress<double>? progress = null)
    {
        return options.SubMode switch
        {
            HealingSubMode.Telea => HealTelea(targetChannel, defectMask, options, usedFallback, statusMessage, progress),
            HealingSubMode.Patch => HealPatchOnly(targetChannel, defectMask, options, usedFallback, statusMessage, progress: progress),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.SubMode, "Unsupported healing sub-mode."),
        };
    }

    private static HealResult HealTelea(
        ImageBuffer targetChannel,
        byte[] defectMask,
        HealOptions options,
        bool usedFallback,
        string? statusMessage,
        IProgress<double>? progress)
    {
        ReportProgress(progress, 15);
        var healed = ChannelRetoucher.InpaintMask(targetChannel, defectMask, options.NormalizedInpaintRadius);
        ReportProgress(progress, 100);
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
        bool guided = false,
        IProgress<double>? progress = null)
    {
        var (result, averageConfidence) = ApplyPatchHealing(
            targetChannel,
            guide1,
            guide2,
            defectMask,
            options,
            guided,
            progress);
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
        HealOptions options,
        IProgress<double>? progress)
    {
        var defectPixelCount = CountMaskedPixels(defectMask);
        var useLargeMaskFastPath = defectPixelCount >= options.NormalizedLargeMaskFastPathPixelThreshold;
        var diagnostics = options.Diagnostics ?? NullProcessingDiagnostics.Instance;
        diagnostics.Log(
            ProcessingLogCategory.PipelineStage,
            useLargeMaskFastPath
                ? $"[retouch] cross-channel large bulk path ({defectPixelCount} px)"
                : $"[retouch] cross-channel component path ({defectPixelCount} px)");

        var result = targetChannel.Clone();
        var width = targetChannel.Width;
        var height = targetChannel.Height;
        using var globalDefectMask = HealingMaskUtils.MaskToMat(defectMask, width, height);
        var components = HealingMaskUtils.FindComponents(defectMask, width, height);
        ReportProgress(progress, 5);

        try
        {
            if (useLargeMaskFastPath && options.QualityMode == AutoCleanQualityMode.Fast)
            {
                ReportProgress(progress, 100);
                return HealFastMode(
                    targetChannel,
                    guide1,
                    guide2,
                    defectMask,
                    options,
                    defectPixelCount,
                    width,
                    height,
                    progress);
            }

            if (useLargeMaskFastPath &&
                TryHealLargeMaskFastPath(
                    targetChannel,
                    guide1,
                    guide2,
                    defectMask,
                    options,
                    defectPixelCount,
                    components,
                    globalDefectMask,
                    result,
                    width,
                    height,
                    progress,
                    out var fastResult))
            {
                ReportProgress(progress, 100);
                return fastResult;
            }

            var tileGroups = HealingTileGroups.Build(components, width, height);
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                $"[retouch] patch: {tileGroups.Count} tiles, {components.Count} components");

            var usedFallback = false;
            var confidenceSum = 0.0f;
            var confidenceCount = 0;
            var pixelCount = width * height;
            var completed = 0;
            var sync = new object();
            var parallelDegree = Environment.ProcessorCount;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelDegree,
            };

            Parallel.ForEach(
                tileGroups,
                parallelOptions,
                group =>
                {
                    foreach (var componentIndex in group)
                    {
                        var work = HealingScratchBuffers.Rent(pixelCount);
                        var patchScratch = HealingScratchBuffers.Rent(pixelCount);
                        var predictionScratch = HealingScratchBuffers.Rent(pixelCount);
                        try
                        {
                            var component = components[componentIndex];
                            var area = HealingMaskUtils.CountNonZero(component);
                            var isLarge = area > options.MaxComponentArea;
                            var localFallback = false;

                            var prediction = options.UseLocalLinearPrediction
                                ? CrossChannelPredictor.PredictComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, predictionScratch)
                                : new PredictionResult(work, 0.0f, false, default);

                            var patch = options.UseGuidedPatchSearch
                                ? PatchHealer.HealComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, guided: true, patchScratch)
                                : CreateIdentityPatch(targetChannel, patchScratch);

                            if (!prediction.Succeeded && !patch.Succeeded)
                            {
                                lock (sync)
                                {
                                    usedFallback = true;
                                }

                                ApplyTeleaComponent(result, targetChannel, component, options, width, height);
                                var teleaDone = Interlocked.Increment(ref completed);
                                ReportProgress(progress, ComponentProgress(teleaDone - 1, components.Count));
                                continue;
                            }

                            targetChannel.CopyNormalizedTo(work);
                            var alpha = prediction.Succeeded
                                ? predictionAlpha(prediction.Confidence, options, isLarge)
                                : 0.0f;

                            if (prediction.Confidence < options.LowConfidenceThreshold)
                            {
                                alpha = options.PredictionAlphaMin;
                                localFallback = true;
                            }

                            if (!prediction.Succeeded)
                            {
                                localFallback = true;
                            }

                            if (!patch.Succeeded)
                            {
                                localFallback = true;
                                patch = PatchHealer.HealComponent(targetChannel, null, null, component, globalDefectMask, options, guided: false, patchScratch);
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

                            HealingTileMerger.ApplyComponent(result, component, work, options.FeatherSigma, width, height);

                            if (localFallback || prediction.Succeeded)
                            {
                                lock (sync)
                                {
                                    if (localFallback)
                                    {
                                        usedFallback = true;
                                    }

                                    if (prediction.Succeeded)
                                    {
                                        confidenceSum += prediction.Confidence;
                                        confidenceCount++;
                                    }
                                }
                            }

                            if (options.DebugOutput)
                            {
                                lock (sync)
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

                            var done = Interlocked.Increment(ref completed);
                            ReportProgress(progress, ComponentProgress(done - 1, components.Count));
                        }
                        finally
                        {
                            HealingScratchBuffers.Return(work);
                            HealingScratchBuffers.Return(patchScratch);
                            HealingScratchBuffers.Return(predictionScratch);
                        }
                    }
                });
            if (options.DebugOutput)
            {
                HealingDebugWriter.SaveFinalDebug(options, targetChannel, result, defectMask, width, height);
            }

            var averageConfidence = confidenceCount > 0 ? confidenceSum / confidenceCount : 0.0f;
            ReportProgress(progress, 100);
            return new HealResult(
                result,
                defectMask,
                averageConfidence,
                UsedCrossChannel: true,
                UsedFallback: usedFallback,
                StatusMessage: $"Cross-channel: parallel patch ({components.Count} components, {defectPixelCount} px).");
        }
        finally
        {
            foreach (var component in components)
            {
                component.Dispose();
            }
        }
    }

    private const float MinWeightedPrediction = 0.01f;

    private static HealResult HealFastMode(
        ImageBuffer targetChannel,
        ImageBuffer guide1,
        ImageBuffer guide2,
        byte[] defectMask,
        HealOptions options,
        int defectPixelCount,
        int width,
        int height,
        IProgress<double>? progress)
    {
        var diagnostics = options.Diagnostics ?? NullProcessingDiagnostics.Instance;
        var pixelCount = targetChannel.PixelCount;
        float[] targetValues;
        float[] guide1Values;
        float[] guide2Values;

        if (options.SessionCache?.TryGet(out targetValues, out guide1Values, out guide2Values) == true)
        {
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                "[retouch] reuse detect normalization cache");
        }
        else
        {
            targetValues = new float[pixelCount];
            guide1Values = new float[pixelCount];
            guide2Values = new float[pixelCount];
            PixelParallel.Invoke(
                () => targetChannel.CopyNormalizedTo(targetValues),
                () => guide1.CopyNormalizedTo(guide1Values),
                () => guide2.CopyNormalizedTo(guide2Values));
        }

        ReportProgress(progress, 20);
        var model = LinearModelFitter.FitMasked(
            targetValues,
            guide1Values,
            guide2Values,
            defectMask,
            options.UseRobustFit);

        var result = targetChannel.Clone();
        if (model.Count < options.MinTrainingPixels)
        {
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                $"[retouch] fast mode: insufficient training={model.Count} < {options.MinTrainingPixels}, Telea fallback");
            var telea = ChannelRetoucher.InpaintMask(targetChannel, defectMask, options.NormalizedInpaintRadius);
            ReportProgress(progress, 100);
            return new HealResult(
                telea,
                defectMask,
                UsedCrossChannel: true,
                UsedFallback: true,
                StatusMessage: $"Fast auto-clean: Telea fallback ({defectPixelCount} px).");
        }

        var confidence = CalculateModelConfidence(targetChannel, targetValues, guide1Values, guide2Values, defectMask, model);
        diagnostics.Log(
            ProcessingLogCategory.PipelineStage,
            $"[retouch] fast mode: confidence={confidence:F2} (training={model.Count:N0} px)");

        ReportProgress(progress, 35);
        var globalPrediction = new float[pixelCount];
        var backend = ImageComputeBackendFactory.CreateBest(options.Diagnostics);
        var usedBackend = backend.TryPredictMasked(
            targetValues,
            guide1Values,
            guide2Values,
            defectMask,
            model.A,
            model.B,
            model.C,
            globalPrediction);
        if (!usedBackend)
        {
            diagnostics.Log(
                ProcessingLogCategory.ComputeBackend,
                "[compute] PredictMasked: all backends failed → CPU inline");
            FillMaskedPredictionCpu(targetValues, guide1Values, guide2Values, defectMask, model, globalPrediction);
        }

        var highConfidenceMask = new byte[pixelCount];
        var lowConfidenceMask = new byte[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            if (defectMask[i] == 0)
            {
                continue;
            }

            if (globalPrediction[i] >= MinWeightedPrediction)
            {
                highConfidenceMask[i] = 1;
            }
            else
            {
                lowConfidenceMask[i] = 1;
            }
        }

        var usedFallback = false;
        if (highConfidenceMask.Any(value => value > 0))
        {
            ApplyPredictionWithFeather(result, highConfidenceMask, globalPrediction, options.FeatherSigma, width, height);
        }

        if (lowConfidenceMask.Any(value => value > 0))
        {
            usedFallback = true;
            var telea = ChannelRetoucher.InpaintMask(targetChannel, lowConfidenceMask, options.NormalizedInpaintRadius);
            var teleaValues = new float[pixelCount];
            telea.CopyNormalizedTo(teleaValues);
            ApplyPredictionWithFeather(result, lowConfidenceMask, teleaValues, options.FeatherSigma, width, height);
        }

        ReportProgress(progress, 100);
        return new HealResult(
            result,
            defectMask,
            confidence,
            UsedCrossChannel: true,
            UsedFallback: usedFallback,
            StatusMessage: $"Fast auto-clean: prediction + Telea ({defectPixelCount} px).");
    }

    private static void ApplyPredictionWithFeather(
        ImageBuffer result,
        byte[] mask,
        float[] values,
        float featherSigma,
        int width,
        int height)
    {
        using var maskMat = HealingMaskUtils.MaskToMat(mask, width, height);
        using var softMask = HealingMaskUtils.BuildSoftMask(maskMat, featherSigma);
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

    private static bool TryHealLargeMaskFastPath(
        ImageBuffer targetChannel,
        ImageBuffer guide1,
        ImageBuffer guide2,
        byte[] defectMask,
        HealOptions options,
        int defectPixelCount,
        IReadOnlyList<Mat> components,
        Mat globalDefectMask,
        ImageBuffer result,
        int width,
        int height,
        IProgress<double>? progress,
        out HealResult healResult)
    {
        healResult = null!;
        var diagnostics = options.Diagnostics ?? NullProcessingDiagnostics.Instance;
        var pixelCount = targetChannel.PixelCount;
        float[] targetValues;
        float[] guide1Values;
        float[] guide2Values;

        if (options.SessionCache?.TryGet(out targetValues, out guide1Values, out guide2Values) == true)
        {
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                "[retouch] reuse detect normalization cache");
        }
        else
        {
            targetValues = new float[pixelCount];
            guide1Values = new float[pixelCount];
            guide2Values = new float[pixelCount];
            PixelParallel.Invoke(
                () => targetChannel.CopyNormalizedTo(targetValues),
                () => guide1.CopyNormalizedTo(guide1Values),
                () => guide2.CopyNormalizedTo(guide2Values));
        }

        ReportProgress(progress, 20);
        var model = LinearModelFitter.FitMasked(
            targetValues,
            guide1Values,
            guide2Values,
            defectMask,
            options.UseRobustFit);
        if (model.Count < options.MinTrainingPixels)
        {
            options.Diagnostics?.Log(
                ProcessingLogCategory.PipelineStage,
                $"[retouch] fast path rejected: training={model.Count} < {options.MinTrainingPixels}");
            return false;
        }

        var confidence = CalculateModelConfidence(targetChannel, targetValues, guide1Values, guide2Values, defectMask, model);
        var softFastPath = options.AllowSoftFastPath &&
                           options.QualityMode == AutoCleanQualityMode.Quality &&
                           confidence < options.LowConfidenceThreshold;

        if (confidence < options.LowConfidenceThreshold && !softFastPath)
        {
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                $"[retouch] fast path rejected: confidence={confidence:F2} < {options.LowConfidenceThreshold:F2} (training={model.Count:N0} px)");
            return false;
        }

        if (softFastPath)
        {
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                $"[retouch] fast path soft-accept: confidence={confidence:F2} < {options.LowConfidenceThreshold:F2}, alpha scaled");
        }
        else
        {
            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                $"[retouch] fast path ok: confidence={confidence:F2} (training={model.Count:N0} px)");
        }

        ReportProgress(progress, 35);
        var globalPrediction = new float[pixelCount];
        var backend = ImageComputeBackendFactory.CreateBest(options.Diagnostics);
        var usedBackend = backend.TryPredictMasked(
            targetValues,
            guide1Values,
            guide2Values,
            defectMask,
            model.A,
            model.B,
            model.C,
            globalPrediction);
        if (!usedBackend)
        {
            options.Diagnostics?.Log(
                ProcessingLogCategory.ComputeBackend,
                "[compute] PredictMasked: all backends failed → CPU inline");
            FillMaskedPredictionCpu(targetValues, guide1Values, guide2Values, defectMask, model, globalPrediction);
        }

        var backendLabel = usedBackend ? "accelerated" : "CPU";

        var baseAlpha = softFastPath
            ? predictionAlpha(confidence, options, isLarge: true) *
              Math.Clamp(confidence / options.LowConfidenceThreshold, options.PredictionAlphaMin, 1.0f)
            : predictionAlpha(confidence, options, isLarge: true);
        var tileGroups = HealingTileGroups.Build(components, width, height);
        diagnostics.Log(
            ProcessingLogCategory.PipelineStage,
            $"[retouch] patch: {tileGroups.Count} tiles, {components.Count} components");

        var usedFallback = false;
        var confidenceSum = 0.0;
        var confidenceCount = 0;
        var completed = 0;
        var sync = new object();
        var parallelDegree = Environment.ProcessorCount;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelDegree,
        };

        Parallel.ForEach(
            tileGroups,
            parallelOptions,
            group =>
            {
                foreach (var componentIndex in group)
                {
                    var work = HealingScratchBuffers.Rent(pixelCount);
                    var patchScratch = HealingScratchBuffers.Rent(pixelCount);
                    var predictionScratch = HealingScratchBuffers.Rent(pixelCount);
                    try
                    {
                        var component = components[componentIndex];
                        targetChannel.CopyNormalizedTo(work);

                        var patch = options.UseGuidedPatchSearch
                            ? PatchHealer.HealComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, guided: true, patchScratch)
                            : CreateIdentityPatch(targetChannel, patchScratch);
                        if (!patch.Succeeded)
                        {
                            patch = PatchHealer.HealComponent(targetChannel, null, null, component, globalDefectMask, options, guided: false, patchScratch);
                        }

                        var area = HealingMaskUtils.CountNonZero(component);
                        var isLarge = area > options.MaxComponentArea;
                        var localFallback = false;

                        var needsLocalPrediction = false;
                        if (options.UseLocalLinearPrediction)
                        {
                            for (var y = 0; y < height && !needsLocalPrediction; y++)
                            {
                                for (var x = 0; x < width; x++)
                                {
                                    if (component.At<byte>(y, x) == 0)
                                    {
                                        continue;
                                    }

                                    if (globalPrediction[(y * width) + x] < MinWeightedPrediction)
                                    {
                                        needsLocalPrediction = true;
                                        break;
                                    }
                                }
                            }
                        }

                        var localPrediction = needsLocalPrediction
                            ? CrossChannelPredictor.PredictComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, predictionScratch)
                            : new PredictionResult(work, 0.0f, false, default);

                        if (!localPrediction.Succeeded && !patch.Succeeded)
                        {
                            localFallback = true;
                            var componentMaskBytes = ExtractComponentMask(component, width, height);
                            var telea = ChannelRetoucher.InpaintMask(targetChannel, componentMaskBytes, options.NormalizedInpaintRadius);
                            telea.CopyNormalizedTo(work);
                        }
                        else
                        {
                            for (var y = 0; y < height; y++)
                            {
                                for (var x = 0; x < width; x++)
                                {
                                    if (component.At<byte>(y, x) == 0)
                                    {
                                        continue;
                                    }

                                    var index = (y * width) + x;
                                    float predicted;
                                    float alpha;
                                    if (localPrediction.Succeeded)
                                    {
                                        predicted = localPrediction.Prediction[index];
                                        alpha = predictionAlpha(localPrediction.Confidence, options, isLarge);
                                        if (localPrediction.Confidence < options.LowConfidenceThreshold)
                                        {
                                            alpha = options.PredictionAlphaMin;
                                            localFallback = true;
                                        }
                                    }
                                    else
                                    {
                                        predicted = globalPrediction[index];
                                        alpha = predicted < MinWeightedPrediction ? 0.0f : baseAlpha;
                                        if (alpha <= 0.0f)
                                        {
                                            localFallback = true;
                                        }
                                    }

                                    if (!localPrediction.Succeeded || !patch.Succeeded)
                                    {
                                        localFallback = true;
                                    }

                                    var patched = patch.Succeeded ? patch.PatchValues[index] : work[index];
                                    work[index] = (alpha * predicted) + ((1.0f - alpha) * patched);
                                }
                            }
                        }

                        HealingTileMerger.ApplyComponent(result, component, work, options.FeatherSigma, width, height);

                        lock (sync)
                        {
                            if (localFallback)
                            {
                                usedFallback = true;
                            }

                            if (localPrediction.Succeeded)
                            {
                                confidenceSum += localPrediction.Confidence;
                                confidenceCount++;
                            }
                            else if (patch.Succeeded)
                            {
                                confidenceSum += patch.Confidence;
                                confidenceCount++;
                            }
                        }

                        var done = Interlocked.Increment(ref completed);
                        ReportProgress(progress, ComponentProgress(done - 1, components.Count));
                    }
                    finally
                    {
                        HealingScratchBuffers.Return(work);
                        HealingScratchBuffers.Return(patchScratch);
                        HealingScratchBuffers.Return(predictionScratch);
                    }
                }
            });
        ReportProgress(progress, 95);

        healResult = new HealResult(
            result,
            defectMask,
            confidence,
            UsedCrossChannel: true,
            UsedFallback: usedFallback,
            StatusMessage:
            $"Large auto-clean: {backendLabel} prediction + parallel patch ({components.Count} components, {defectPixelCount} px).");
        return true;
    }

    private static float CalculateModelConfidence(
        ImageBuffer targetChannel,
        float[] targetValues,
        float[] guide1Values,
        float[] guide2Values,
        byte[] defectMask,
        LinearModel model)
    {
        var totalError = 0.0;
        var totalCount = 0;
        var sync = new object();
        PixelParallel.For(
            0,
            targetValues.Length,
            static () => new ErrorAccumulator(),
            (i, local) =>
            {
                if (defectMask[i] > 0)
                {
                    return local;
                }

                var predicted = Math.Clamp(
                    LinearModelFitter.Predict(model, guide1Values[i], guide2Values[i]),
                    0.0f,
                    1.0f);
                local.ErrorSum += Math.Abs(targetValues[i] - predicted);
                local.Count++;
                return local;
            },
            local =>
            {
                if (local.Count == 0)
                {
                    return;
                }

                lock (sync)
                {
                    totalError += local.ErrorSum;
                    totalCount += local.Count;
                }
            });

        if (totalCount == 0)
        {
            return 0.0f;
        }

        var meanAbsError = (float)(totalError / totalCount);
        return 1.0f - Math.Clamp(meanAbsError / targetChannel.MaxAllowedHealError, 0.0f, 1.0f);
    }

    private static void FillMaskedPredictionCpu(
        float[] targetValues,
        float[] guide1Values,
        float[] guide2Values,
        byte[] defectMask,
        LinearModel model,
        float[] output)
    {
        PixelParallel.For(0, output.Length, i =>
        {
            output[i] = defectMask[i] > 0
                ? Math.Clamp(LinearModelFitter.Predict(model, guide1Values[i], guide2Values[i]), 0.0f, 1.0f)
                : targetValues[i];
        });
    }

    private struct ErrorAccumulator
    {
        public double ErrorSum;

        public int Count;
    }

    private static int CountMaskedPixels(byte[] defectMask)
    {
        var total = 0;
        PixelParallel.For(
            0,
            defectMask.Length,
            static () => 0,
            (i, local) => local + (defectMask[i] > 0 ? 1 : 0),
            local => System.Threading.Interlocked.Add(ref total, local));

        return total;
    }

    private static (ImageBuffer Image, float AverageConfidence) ApplyPatchHealing(
        ImageBuffer targetChannel,
        ImageBuffer? guide1,
        ImageBuffer? guide2,
        byte[] defectMask,
        HealOptions options,
        bool guided,
        IProgress<double>? progress)
    {
        var result = targetChannel.Clone();
        var width = targetChannel.Width;
        var height = targetChannel.Height;
        using var globalDefectMask = HealingMaskUtils.MaskToMat(defectMask, width, height);
        var components = HealingMaskUtils.FindComponents(defectMask, width, height);
        ReportProgress(progress, 5);
        var confidenceSum = 0.0f;
        var confidenceCount = 0;

        try
        {
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var component = components[componentIndex];
                var patch = PatchHealer.HealComponent(targetChannel, guide1, guide2, component, globalDefectMask, options, guided);
                if (!patch.Succeeded)
                {
                    ApplyTeleaComponent(result, targetChannel, component, options, width, height);
                    ReportProgress(progress, ComponentProgress(componentIndex, components.Count));
                    continue;
                }

                confidenceSum += patch.Confidence;
                confidenceCount++;
                ApplyComponentValues(result, component, patch.PatchValues, options.FeatherSigma, width, height);
                ReportProgress(progress, ComponentProgress(componentIndex, components.Count));
            }
        }
        finally
        {
            foreach (var component in components)
            {
                component.Dispose();
            }
        }

        ReportProgress(progress, 100);
        return (result, confidenceCount > 0 ? confidenceSum / confidenceCount : 0.0f);
    }

    private static double ComponentProgress(int componentIndex, int componentCount)
    {
        return componentCount == 0
            ? 100.0
            : 5.0 + (90.0 * (componentIndex + 1) / componentCount);
    }

    private static void ReportProgress(IProgress<double>? progress, double value)
    {
        progress?.Report(Math.Clamp(value, 0.0, 100.0));
    }

    private static PatchHealResult CreateIdentityPatch(ImageBuffer targetChannel, float[] patchValues)
    {
        targetChannel.CopyNormalizedTo(patchValues);
        return new PatchHealResult(patchValues, 0.0f, false, default);
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
        int height) =>
        HealingTileMerger.ApplyComponent(result, componentMask, values, featherSigma, width, height);

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
