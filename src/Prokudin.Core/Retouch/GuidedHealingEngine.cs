using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

/// <summary>
/// Evidence-bounded cross-channel repair. Guide pixels only contribute a
/// local structural delta; target-channel tone always comes from unmasked
/// target samples. This path deliberately works in normalized native samples
/// and never round-trips a UInt16 channel through an 8-bit inpaint buffer.
/// </summary>
internal static class GuidedHealingEngine
{
    public static HealResult Heal(
        ImageBuffer target,
        HealingGuide guide1,
        HealingGuide guide2,
        byte[] defectMask,
        RetouchProvenanceMap targetProvenance,
        HealOptions options,
        IProgress<double>? progress)
    {
        Validate(target, guide1, guide2, defectMask, targetProvenance);

        var source = new float[target.PixelCount];
        target.CopyNormalizedTo(source);
        var repaired = (float[])source.Clone();
        var guide1Values = new float[target.PixelCount];
        var guide2Values = new float[target.PixelCount];
        guide1.Image.CopyNormalizedTo(guide1Values);
        guide2.Image.CopyNormalizedTo(guide2Values);

        var components = FindComponents(defectMask, target.Width, target.Height);
        var outputProvenance = targetProvenance.Clone();
        var compactCount = 0;
        var scratchCount = 0;
        var lowConfidenceCount = 0;
        var excludedGuides = 0;
        var boundarySegments = 0;
        var confidenceSum = 0.0f;
        var confidenceCount = 0;

        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            var component = components[componentIndex];
            var isScratch = IsScratch(component);
            if (isScratch)
            {
                scratchCount++;
            }
            else
            {
                compactCount++;
            }

            var regions = isScratch
                ? SegmentScratch(component, source, defectMask, target.Width, target.Height)
                : [component];
            boundarySegments += isScratch ? regions.Count : 0;
            foreach (var region in regions)
            {
                var outcome = RepairRegion(
                    region,
                    component,
                    isScratch,
                    component.Area > options.MaxComponentArea,
                    target,
                    guide1,
                    guide2,
                    source,
                    repaired,
                    guide1Values,
                    guide2Values,
                    defectMask,
                    outputProvenance,
                    options);
                excludedGuides += outcome.ExcludedGuides;
                lowConfidenceCount += outcome.LowConfidence ? 1 : 0;
                confidenceSum += outcome.Confidence;
                confidenceCount++;
            }
            progress?.Report(((componentIndex + 1) * 100.0) / components.Count);
        }

        var averageConfidence = confidenceCount == 0 ? 0.0f : confidenceSum / confidenceCount;
        var summary = new GuidedHealingSummary(compactCount, scratchCount, lowConfidenceCount, excludedGuides, boundarySegments);
        var status = summary.HasLowConfidence
            ? "Healing confidence is low for part of this repair; used conservative local evidence. Undo to revise."
            : $"Guided healing repaired {compactCount} point component(s) and {scratchCount} scratch component(s).";
        var image = ImageBuffer.FromNormalized(target.Width, target.Height, repaired, target.Format);
        if (options.DebugOutput)
        {
            HealingDebugWriter.SaveFinalDebug(options, target, image, defectMask, target.Width, target.Height);
        }

        return new HealResult(
            image,
            defectMask,
            averageConfidence,
            UsedCrossChannel: true,
            UsedFallback: summary.HasLowConfidence,
            StatusMessage: status,
            Provenance: outputProvenance,
            GuidedSummary: summary);
    }

    private static void Validate(
        ImageBuffer target,
        HealingGuide guide1,
        HealingGuide guide2,
        byte[] mask,
        RetouchProvenanceMap targetProvenance)
    {
        if (mask.Length != target.PixelCount || targetProvenance.PixelCount != target.PixelCount)
        {
            throw new ArgumentException("Mask and target provenance must match target dimensions.");
        }

        foreach (var guide in new[] { guide1, guide2 })
        {
            if (guide.Image.Width != target.Width || guide.Image.Height != target.Height || guide.Provenance.PixelCount != target.PixelCount)
            {
                throw new ArgumentException("Guides and their provenance must match target dimensions.");
            }
        }
    }

    private static RegionOutcome RepairRegion(
        Component region,
        Component fullComponent,
        bool isScratch,
        bool prohibitSceneCompletion,
        ImageBuffer target,
        HealingGuide guide1,
        HealingGuide guide2,
        float[] source,
        float[] repaired,
        float[] guide1Values,
        float[] guide2Values,
        byte[] defectMask,
        RetouchProvenanceMap outputProvenance,
        HealOptions options)
    {
        var context = CollectContext(region, defectMask, target.Width, target.Height, options.NormalizedContextRadius);
        var assessment1 = AssessGuide(guide1, guide1Values, source, context, options);
        var assessment2 = AssessGuide(guide2, guide2Values, source, context, options);
        var agreement = CalculateAgreement(assessment1, assessment2);
        var model1 = CanUseGuide(assessment1, assessment2, agreement)
            ? FitStructuralModel(source, guide1Values, assessment1.Context, options.UseRobustFit)
            : StructuralModel.None;
        var model2 = CanUseGuide(assessment2, assessment1, agreement)
            ? FitStructuralModel(source, guide2Values, assessment2.Context, options.UseRobustFit)
            : StructuralModel.None;
        var targetTone = Median(source, context);
        var guideCount = (model1.IsUsable ? 1 : 0) + (model2.IsUsable ? 1 : 0);
        var modelConfidence = CalculateConfidence(model1, model2, guideCount, agreement, target.MaxAllowedHealError);
        var hasStructuralEvidenceForEveryPixel = region.Indices.All(index =>
            (model1.IsUsable && CanUseGuidePixel(guide1.Provenance[index], guide2.Provenance[index], model2.IsUsable)) ||
            (model2.IsUsable && CanUseGuidePixel(guide2.Provenance[index], guide1.Provenance[index], model1.IsUsable)));
        var lowConfidence = prohibitSceneCompletion ||
            guideCount == 0 ||
            !hasStructuralEvidenceForEveryPixel ||
            modelConfidence < options.LowConfidenceThreshold;

        foreach (var index in region.Indices)
        {
            var local = isScratch
                ? TransverseEstimate(index, fullComponent, source, defectMask, target.Width, target.Height, options.NormalizedContextRadius)
                : LocalEstimate(index, source, defectMask, target.Width, target.Height, options.NormalizedContextRadius);
            var structural = prohibitSceneCompletion
                ? null
                : PredictStructure(
                    index,
                    targetTone,
                    model1,
                    model2,
                    guide1Values,
                    guide2Values,
                    guide1.Provenance,
                    guide2.Provenance);

            // A large or ambiguous interior has no trustworthy local evidence.
            // Retain the working sample rather than synthesize a scene from a
            // guide prediction or a component-wide median.
            var repairedValue = structural ?? local?.Value ?? source[index];
            if (isScratch && local.HasValue && structural.HasValue)
            {
                repairedValue = local.Value.IsBoundary
                    ? structural.Value
                    : (local.Value.Value * 0.72f) + (structural.Value * 0.28f);
            }
            else if (local.HasValue && structural.HasValue && lowConfidence)
            {
                repairedValue = (local.Value.Value * 0.8f) + (structural.Value * 0.2f);
            }

            repaired[index] = Math.Clamp(repairedValue, 0.0f, 1.0f);
        }

        outputProvenance.MarkIndexes(region.Indices, lowConfidence
            ? RetouchProvenance.LowConfidenceHealing
            : RetouchProvenance.HighConfidenceHealing);
        return new RegionOutcome(
            modelConfidence,
            lowConfidence,
            (assessment1.Eligible ? 0 : 1) + (assessment2.Eligible ? 0 : 1));
    }

    private static List<Component> FindComponents(byte[] mask, int width, int height)
    {
        var seen = new bool[mask.Length];
        var result = new List<Component>();
        for (var start = 0; start < mask.Length; start++)
        {
            if (mask[start] == 0 || seen[start])
            {
                continue;
            }

            var queue = new Queue<int>();
            var pixels = new List<int>();
            queue.Enqueue(start);
            seen[start] = true;
            var minX = width;
            var minY = height;
            var maxX = 0;
            var maxY = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                pixels.Add(current);
                var x = current % width;
                var y = current / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var next = (ny * width) + nx;
                        if (mask[next] > 0 && !seen[next])
                        {
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            result.Add(Component.FromIndexes(pixels, width));
        }

        return result;
    }

    private static bool IsScratch(Component component)
    {
        var shortSide = Math.Min(component.Width, component.Height);
        var longSide = Math.Max(component.Width, component.Height);
        if (component.Area < 6 || longSide < 3)
        {
            return false;
        }

        if (longSide >= shortSide * 3)
        {
            return true;
        }

        // Bounding boxes miss diagonal scratches. Use principal-axis spread as
        // a rotation-independent elongated-component classification signal.
        var centerX = component.Indices.Average(index => index % component.SourceWidth);
        var centerY = component.Indices.Average(index => index / component.SourceWidth);
        var xx = 0.0;
        var yy = 0.0;
        var xy = 0.0;
        foreach (var index in component.Indices)
        {
            var dx = (index % component.SourceWidth) - centerX;
            var dy = (index / component.SourceWidth) - centerY;
            xx += dx * dx;
            yy += dy * dy;
            xy += dx * dy;
        }

        var trace = xx + yy;
        var determinant = (xx * yy) - (xy * xy);
        var discriminant = Math.Sqrt(Math.Max(0, (trace * trace) - (4 * determinant)));
        var major = (trace + discriminant) * 0.5;
        var minor = (trace - discriminant) * 0.5;
        return major >= Math.Max(1.0, minor * 8.0);
    }

    private static List<Component> SegmentScratch(Component component, float[] source, byte[] mask, int width, int height)
    {
        var alongHorizontal = component.Width >= component.Height;
        var groups = component.Indices
            .GroupBy(index => alongHorizontal ? index % width : index / width)
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToList();
        if (groups.Count <= 1)
        {
            return [component];
        }

        var regions = new List<Component>();
        var current = new List<int>();
        float? previousTone = null;
        foreach (var group in groups)
        {
            var transverse = group
                .Select(index => TransverseEstimate(index, component, source, mask, width, height, 4)?.Value)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .DefaultIfEmpty(float.NaN)
                .Average();
            if (current.Count > 0 && !float.IsNaN(transverse) && previousTone is { } tone && Math.Abs(transverse - tone) > 0.12f)
            {
                regions.Add(Component.FromIndexes(current, width));
                current = [];
            }

            current.AddRange(group);
            if (!float.IsNaN(transverse))
            {
                previousTone = transverse;
            }
        }

        if (current.Count > 0)
        {
            regions.Add(Component.FromIndexes(current, width));
        }

        return regions.Count > 0 ? regions : [component];
    }

    private static List<int> CollectContext(Component component, byte[] mask, int width, int height, int radius)
    {
        var context = new List<int>();
        var x0 = Math.Max(0, component.MinX - radius);
        var y0 = Math.Max(0, component.MinY - radius);
        var x1 = Math.Min(width - 1, component.MaxX + radius);
        var y1 = Math.Min(height - 1, component.MaxY + radius);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var index = (y * width) + x;
                if (mask[index] == 0)
                {
                    context.Add(index);
                }
            }
        }

        return context;
    }

    private static GuideAssessment AssessGuide(
        HealingGuide guide,
        float[] guideValues,
        float[] targetValues,
        IReadOnlyList<int> context,
        HealOptions options)
    {
        var usable = new List<int>(context.Count);
        var unknown = false;
        foreach (var index in context)
        {
            switch (guide.Provenance[index])
            {
                case RetouchProvenance.Original:
                case RetouchProvenance.HighConfidenceHealing:
                    usable.Add(index);
                    break;
                case RetouchProvenance.Unknown:
                    usable.Add(index);
                    unknown = true;
                    break;
            }
        }

        if (usable.Count < Math.Max(12, options.MinTrainingPixels / 2) || usable.Count < context.Count * 0.75)
        {
            return GuideAssessment.Ineligible;
        }

        var model = FitStructuralModel(targetValues, guideValues, usable, options.UseRobustFit);
        return !model.IsUsable
            ? GuideAssessment.Ineligible
            : new GuideAssessment(true, unknown, usable, model);
    }

    private static float CalculateAgreement(GuideAssessment first, GuideAssessment second)
    {
        if (!first.Eligible || !second.Eligible)
        {
            return 0.0f;
        }

        var shared = first.Context.Intersect(second.Context).ToArray();
        if (shared.Length < 12)
        {
            return 0.0f;
        }

        // The structural models express target response, so agreement is based
        // on their independently fitted scale and error rather than guide tone.
        var scaleDifference = Math.Abs(first.Model.Slope - second.Model.Slope);
        var error = (first.Model.MeanAbsoluteError + second.Model.MeanAbsoluteError) * 0.5f;
        return Math.Clamp(1.0f - (scaleDifference * 0.35f) - (error * 5.0f), 0.0f, 1.0f);
    }

    private static bool CanUseGuide(GuideAssessment candidate, GuideAssessment other, float agreement)
    {
        if (!candidate.Eligible)
        {
            return false;
        }

        // A legacy Unknown guide is evidence only when the other guide agrees;
        // it can never become a sole donor.
        if (candidate.HasUnknownProvenance)
        {
            return other.Eligible && agreement >= 0.55f;
        }

        return !other.Eligible || agreement >= 0.30f;
    }

    private static StructuralModel FitStructuralModel(float[] target, float[] guide, IReadOnlyList<int> context, bool robustFit)
    {
        if (context.Count < 12)
        {
            return StructuralModel.None;
        }

        var targetBase = Median(target, context);
        var guideBase = Median(guide, context);
        var numerator = 0.0;
        var denominator = 0.0;
        foreach (var index in context)
        {
            var targetDelta = target[index] - targetBase;
            var guideDelta = guide[index] - guideBase;
            numerator += targetDelta * guideDelta;
            denominator += guideDelta * guideDelta;
        }

        if (denominator < 1e-7)
        {
            return StructuralModel.None;
        }

        var slope = (float)(numerator / denominator);
        var residuals = new float[context.Count];
        var error = 0.0f;
        var residualIndex = 0;
        foreach (var index in context)
        {
            var residual = Math.Abs((target[index] - targetBase) - (slope * (guide[index] - guideBase)));
            residuals[residualIndex++] = residual;
            error += residual;
        }

        if (robustFit && context.Count >= 24)
        {
            Array.Sort(residuals);
            var medianResidual = residuals[residuals.Length / 2];
            var cutoff = Math.Max(0.004f, medianResidual * 2.5f);
            var inliers = context
                .Where((index, indexInContext) => Math.Abs((target[index] - targetBase) - (slope * (guide[index] - guideBase))) <= cutoff)
                .ToArray();
            if (inliers.Length >= Math.Max(12, context.Count / 2))
            {
                return FitStructuralModel(target, guide, inliers, robustFit: false);
            }
        }

        return new StructuralModel(targetBase, guideBase, slope, error / context.Count, true);
    }

    private static float CalculateConfidence(StructuralModel first, StructuralModel second, int guideCount, float agreement, float maxError)
    {
        if (guideCount == 0)
        {
            return 0.0f;
        }

        var error = guideCount == 2
            ? (first.MeanAbsoluteError + second.MeanAbsoluteError) * 0.5f
            : first.IsUsable ? first.MeanAbsoluteError : second.MeanAbsoluteError;
        var fit = 1.0f - Math.Clamp(error / Math.Max(maxError, 0.001f), 0.0f, 1.0f);
        return guideCount == 2 ? fit * Math.Max(agreement, 0.25f) : fit * 0.55f;
    }

    private static float? PredictStructure(
        int index,
        float targetTone,
        StructuralModel first,
        StructuralModel second,
        float[] guide1,
        float[] guide2,
        RetouchProvenanceMap provenance1,
        RetouchProvenanceMap provenance2)
    {
        var firstPrediction = first.IsUsable && CanUseGuidePixel(provenance1[index], provenance2[index], second.IsUsable)
            ? targetTone + (first.Slope * (guide1[index] - first.GuideBase))
            : (float?)null;
        var secondPrediction = second.IsUsable && CanUseGuidePixel(provenance2[index], provenance1[index], first.IsUsable)
            ? targetTone + (second.Slope * (guide2[index] - second.GuideBase))
            : (float?)null;
        return (firstPrediction, secondPrediction) switch
        {
            ({ } a, { } b) => (a + b) * 0.5f,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null,
        };
    }

    private static bool CanUseGuidePixel(RetouchProvenance provenance, RetouchProvenance otherProvenance, bool otherModelUsable) =>
        provenance switch
        {
            RetouchProvenance.Original or RetouchProvenance.HighConfidenceHealing => true,
            RetouchProvenance.Unknown => otherModelUsable && otherProvenance is RetouchProvenance.Original or RetouchProvenance.HighConfidenceHealing or RetouchProvenance.Unknown,
            _ => false,
        };

    private static LocalSample? LocalEstimate(int index, float[] source, byte[] mask, int width, int height, int radius)
    {
        var x = index % width;
        var y = index / width;
        var samples = new List<float>(4);
        AddNearest(samples, x, y, -1, 0, source, mask, width, height, radius);
        AddNearest(samples, x, y, 1, 0, source, mask, width, height, radius);
        AddNearest(samples, x, y, 0, -1, source, mask, width, height, radius);
        AddNearest(samples, x, y, 0, 1, source, mask, width, height, radius);
        return samples.Count == 0 ? null : new LocalSample(samples.Average(), Range(samples) > 0.12f);
    }

    private static LocalSample? TransverseEstimate(int index, Component component, float[] source, byte[] mask, int width, int height, int radius)
    {
        var x = index % width;
        var y = index / width;
        var alongHorizontal = component.Width >= component.Height;
        var first = FindNearest(x, y, alongHorizontal ? 0 : -1, alongHorizontal ? -1 : 0, source, mask, width, height, radius);
        var second = FindNearest(x, y, alongHorizontal ? 0 : 1, alongHorizontal ? 1 : 0, source, mask, width, height, radius);
        return (first, second) switch
        {
            ({ } a, { } b) => new LocalSample((a + b) * 0.5f, Math.Abs(a - b) > 0.12f),
            ({ } a, null) => new LocalSample(a, false),
            (null, { } b) => new LocalSample(b, false),
            _ => LocalEstimate(index, source, mask, width, height, radius),
        };
    }

    private static void AddNearest(List<float> values, int x, int y, int dx, int dy, float[] source, byte[] mask, int width, int height, int radius)
    {
        if (FindNearest(x, y, dx, dy, source, mask, width, height, radius) is { } value)
        {
            values.Add(value);
        }
    }

    private static float? FindNearest(int x, int y, int dx, int dy, float[] source, byte[] mask, int width, int height, int radius)
    {
        for (var step = 1; step <= radius; step++)
        {
            var sx = x + (dx * step);
            var sy = y + (dy * step);
            if (sx < 0 || sy < 0 || sx >= width || sy >= height)
            {
                return null;
            }

            var sampleIndex = (sy * width) + sx;
            if (mask[sampleIndex] == 0)
            {
                return source[sampleIndex];
            }
        }

        return null;
    }

    private static int CountScratchBoundarySegments(Component component, float[] source, byte[] mask, int width, int height)
    {
        var alongHorizontal = component.Width >= component.Height;
        var ordered = component.Indices.OrderBy(index => alongHorizontal ? index % width : index / width);
        var segments = 1;
        var previous = float.NaN;
        foreach (var index in ordered)
        {
            var sample = TransverseEstimate(index, component, source, mask, width, height, 4)?.Value;
            if (sample is { } value && !float.IsNaN(previous) && Math.Abs(value - previous) > 0.12f)
            {
                segments++;
            }

            if (sample is { } current)
            {
                previous = current;
            }
        }

        return segments;
    }

    private static float Median(float[] values, IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0)
        {
            return 0.5f;
        }

        var samples = new float[indexes.Count];
        for (var i = 0; i < indexes.Count; i++)
        {
            samples[i] = values[indexes[i]];
        }

        Array.Sort(samples);
        var middle = samples.Length / 2;
        return samples.Length % 2 == 0 ? (samples[middle - 1] + samples[middle]) * 0.5f : samples[middle];
    }

    private static float Range(IReadOnlyList<float> values) => values.Count == 0 ? 0.0f : values.Max() - values.Min();

    private sealed record Component(List<int> Indices, int MinX, int MinY, int MaxX, int MaxY, int SourceWidth)
    {
        public int Width => (MaxX - MinX) + 1;

        public int Height => (MaxY - MinY) + 1;

        public int Area => Indices.Count;

        public static Component FromIndexes(IEnumerable<int> indexes, int sourceWidth)
        {
            var pixels = indexes.ToList();
            var minX = pixels.Min(index => index % sourceWidth);
            var minY = pixels.Min(index => index / sourceWidth);
            var maxX = pixels.Max(index => index % sourceWidth);
            var maxY = pixels.Max(index => index / sourceWidth);
            return new Component(pixels, minX, minY, maxX, maxY, sourceWidth);
        }

    }

    private readonly record struct GuideAssessment(bool Eligible, bool HasUnknownProvenance, IReadOnlyList<int> Context, StructuralModel Model)
    {
        public static GuideAssessment Ineligible => new(false, false, [], StructuralModel.None);
    }

    private readonly record struct StructuralModel(float TargetBase, float GuideBase, float Slope, float MeanAbsoluteError, bool IsUsable)
    {
        public static StructuralModel None => new(0, 0, 0, 1, false);
    }

    private readonly record struct LocalSample(float Value, bool IsBoundary);

    private readonly record struct RegionOutcome(float Confidence, bool LowConfidence, int ExcludedGuides);
}
