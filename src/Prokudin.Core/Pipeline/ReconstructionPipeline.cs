using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Crop;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Imaging;
using Prokudin.Core.Processing;
using Prokudin.Core.Transform;

namespace Prokudin.Core.Pipeline;

public static class ReconstructionPipeline
{
    public static async Task<IReadOnlyDictionary<ChannelName, ImageBuffer>> LoadProjectChannelsAsync(
        IReadOnlyDictionary<ChannelName, string> paths,
        bool trimBorders = true,
        CancellationToken cancellationToken = default)
    {
        var channels = new Dictionary<ChannelName, ImageBuffer>();
        foreach (var (name, path) in paths)
        {
            var image = await ImageLoader.LoadGrayscaleAsync(path, cancellationToken);
            channels[name] = trimBorders ? ImageLoader.TrimBlackBorders(image) : image;
        }

        return channels;
    }

    public static AlignedChannels RunAutoAlign(
        IReadOnlyDictionary<ChannelName, ImageBuffer> channels,
        AlignOptions settings,
        IProcessingDiagnostics? diagnostics = null)
    {
        if (!channels.TryGetValue(settings.Reference, out var reference))
        {
            throw new ArgumentException($"Unknown reference channel: {settings.Reference}", nameof(settings));
        }

        diagnostics ??= NullProcessingDiagnostics.Instance;
        using var alignScope = diagnostics.BeginScope("RunAutoAlign", ProcessingLogCategory.PipelineStage);

        var aligned = new Dictionary<ChannelName, (ImageBuffer Image, byte[] Mask)>();
        var metadata = new Dictionary<ChannelName, AlignChannelMetadata>();
        var transforms = new Dictionary<ChannelName, ChannelAlignmentTransform>();
        var referenceMask = Ones(reference.Width * reference.Height);

        foreach (var name in new[] { ChannelName.Red, ChannelName.Green, ChannelName.Blue })
        {
            if (name == settings.Reference)
            {
                aligned[name] = (reference.Clone(), (byte[])referenceMask.Clone());
                metadata[name] = new AlignChannelMetadata("reference", 0);
                transforms[name] = ChannelAlignmentTransform.Identity(reference.Width, reference.Height, "reference");
                diagnostics.Log(ProcessingLogCategory.PipelineStage, $"[align] {name}: reference");
                continue;
            }

            diagnostics.Log(
                ProcessingLogCategory.PipelineStage,
                $"[align] {name}: aligning {channels[name].Width}x{channels[name].Height} to reference");
            var result = ChannelAligner.AlignChannel(reference, channels[name], settings, diagnostics);
            aligned[name] = (result.Image, result.Mask);
            metadata[name] = new AlignChannelMetadata(result.TransformKind, result.InlierCount, result.SubpixelShifts);
            if (result.Transform is not null)
            {
                transforms[name] = result.Transform;
            }
        }

        return new AlignedChannels(
            aligned[ChannelName.Red].Image,
            aligned[ChannelName.Green].Image,
            aligned[ChannelName.Blue].Image,
            aligned[ChannelName.Red].Mask,
            aligned[ChannelName.Green].Mask,
            aligned[ChannelName.Blue].Mask,
            metadata,
            transforms);
    }

    public static AlignedChannels ApplyManualToAligned(
        AlignedChannels aligned,
        IReadOnlyDictionary<ChannelName, ManualTransform> manual)
    {
        var red = Apply(ChannelName.Red, aligned.Red, aligned.MaskRed);
        var green = Apply(ChannelName.Green, aligned.Green, aligned.MaskGreen);
        var blue = Apply(ChannelName.Blue, aligned.Blue, aligned.MaskBlue);
        return new AlignedChannels(red.Image, green.Image, blue.Image, red.Mask, green.Mask, blue.Mask, aligned.AlignMetadata, aligned.AlignTransforms);

        (ImageBuffer Image, byte[] Mask) Apply(ChannelName name, ImageBuffer image, byte[] mask)
        {
            return manual.TryGetValue(name, out var transform) && !transform.IsIdentity
                ? ImageTransformer.ApplyManualTransforms(image, mask, transform)
                : (image, mask);
        }
    }

    public static (RgbImageBuffer Rgb, CropInfo CropInfo) BuildRgb(
        AlignedChannels aligned,
        PipelineSettings settings,
        IReadOnlyDictionary<ChannelName, ManualTransform>? manual = null)
    {
        if (manual is not null)
        {
            aligned = ApplyManualToAligned(aligned, manual);
        }

        var diagnostics = settings.Diagnostics ?? NullProcessingDiagnostics.Instance;
        using var buildScope = diagnostics.BeginScope("BuildRgb", ProcessingLogCategory.PipelineStage);

        var red = ChannelExposure.Apply(aligned.Red, settings.Exposure.RedStops, diagnostics);
        var green = ChannelExposure.Apply(aligned.Green, settings.Exposure.GreenStops, diagnostics);
        var blue = ChannelExposure.Apply(aligned.Blue, settings.Exposure.BlueStops, diagnostics);

        using var mergeScope = diagnostics.BeginScope("BuildRgb.merge", ProcessingLogCategory.CpuParallel);
        var (rgb, overlap) = Cropper.MergeChannels(
            red,
            green,
            blue,
            aligned.MaskRed,
            aligned.MaskGreen,
            aligned.MaskBlue);

        var sourceWidth = rgb.Width;
        var sourceHeight = rgb.Height;
        var (cropped, cropInfo) = ApplyCrop(rgb, overlap, settings.Crop);
        var croppedOverlap = Cropper.CropMask(overlap, sourceWidth, sourceHeight, cropInfo);
        var corrected = ColorCorrection.ApplyColorSettings(cropped, settings.Color);
        corrected = ColorCorrection.ApplyLevelsSettings(corrected, settings.Levels);
        corrected = Cropper.EnforceGrayscaleOutsideOverlap(corrected, croppedOverlap);

        if (settings.OutputSize is { } size)
        {
            corrected = ResizeRgb(corrected, size, size);
        }

        if (settings.Sharpen)
        {
            corrected = UnsharpMask(corrected);
        }

        return (corrected, cropInfo);
    }

    public static async Task ReconstructFromPathsAsync(
        string redPath,
        string greenPath,
        string bluePath,
        string outputPath,
        PipelineSettings settings,
        CancellationToken cancellationToken = default)
    {
        var paths = new Dictionary<ChannelName, string>
        {
            [ChannelName.Red] = redPath,
            [ChannelName.Green] = greenPath,
            [ChannelName.Blue] = bluePath,
        };
        var channels = await LoadProjectChannelsAsync(paths, settings.Align.TrimBorders, cancellationToken);
        var aligned = RunAutoAlign(channels, settings.Align, settings.Diagnostics);
        var (rgb, _) = BuildRgb(aligned, settings);
        await ImageLoader.SavePngAsync(outputPath, rgb, cancellationToken);
    }

    public static async Task ReconstructFromTriptychAsync(
        string triptychPath,
        TriptychOrder order,
        string outputPath,
        PipelineSettings settings,
        CancellationToken cancellationToken = default)
    {
        var image = await ImageLoader.LoadGrayscaleAsync(triptychPath, cancellationToken);
        var channels = TriptychSplitter.SplitTriptych(image, order, settings.Align.TrimBorders);
        var aligned = RunAutoAlign(channels, settings.Align, settings.Diagnostics);
        var (rgb, _) = BuildRgb(aligned, settings);
        await ImageLoader.SavePngAsync(outputPath, rgb, cancellationToken);
    }

    private static (RgbImageBuffer Rgb, CropInfo Info) ApplyCrop(RgbImageBuffer rgb, byte[] overlap, CropSettings crop)
    {
        if (crop.SkipCrop)
        {
            var bbox = Cropper.OverlapBoundingBox(overlap, rgb.Width, rgb.Height) ?? (0, 0, rgb.Width, rgb.Height);
            return (rgb, new CropInfo(0, 0, rgb.Width, rgb.Height, bbox.X0, bbox.Y0, bbox.X1, bbox.Y1));
        }

        if (crop.UseManual && crop.ManualX1 > crop.ManualX0 && crop.ManualY1 > crop.ManualY0)
        {
            var x0 = Math.Clamp(crop.ManualX0, 0, rgb.Width - 1);
            var y0 = Math.Clamp(crop.ManualY0, 0, rgb.Height - 1);
            var x1 = Math.Clamp(crop.ManualX1, x0 + 1, rgb.Width);
            var y1 = Math.Clamp(crop.ManualY1, y0 + 1, rgb.Height);
            var width = x1 - x0;
            var height = y1 - y0;
            var pixels = new float[width * height * 3];
            PixelParallel.ForRows(height, y =>
            {
                Array.Copy(rgb.Pixels, (((y0 + y) * rgb.Width) + x0) * 3, pixels, y * width * 3, width * 3);
            });

            var bbox = Cropper.OverlapBoundingBox(overlap, rgb.Width, rgb.Height) ?? (0, 0, rgb.Width, rgb.Height);
            return (new RgbImageBuffer(width, height, pixels), new CropInfo(x0, y0, x1, y1, bbox.X0, bbox.Y0, bbox.X1, bbox.Y1));
        }

        var result = Cropper.CropToContent(rgb, overlap);
        crop.AutoInfo = result.Info;
        return result;
    }

    private static RgbImageBuffer ResizeRgb(RgbImageBuffer source, int width, int height)
    {
        var pixels = new float[width * height * 3];
        var scaleX = source.Width / (float)width;
        var scaleY = source.Height / (float)height;
        PixelParallel.ForRows(height, y =>
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y * scaleY));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x * scaleX));
                for (var c = 0; c < 3; c++)
                {
                    pixels[(((y * width) + x) * 3) + c] = source[sourceX, sourceY, c];
                }
            }
        });

        return new RgbImageBuffer(width, height, pixels);
    }

    private static RgbImageBuffer UnsharpMask(RgbImageBuffer source, float amount = 0.35f, float threshold = 3.0f / 255.0f)
    {
        var blurred = BoxBlur3x3(source);
        var output = source.Clone();
        PixelParallel.For(0, source.Width * source.Height, pixel =>
        {
            var i = pixel * 3;
            var apply = false;
            var redDetail = source.Pixels[i] - blurred.Pixels[i];
            var greenDetail = source.Pixels[i + 1] - blurred.Pixels[i + 1];
            var blueDetail = source.Pixels[i + 2] - blurred.Pixels[i + 2];
            apply |= Math.Abs(redDetail) >= threshold;
            apply |= Math.Abs(greenDetail) >= threshold;
            apply |= Math.Abs(blueDetail) >= threshold;

            if (!apply)
            {
                return;
            }

            output.Pixels[i] = Math.Clamp(source.Pixels[i] + (amount * redDetail), 0.0f, 1.0f);
            output.Pixels[i + 1] = Math.Clamp(source.Pixels[i + 1] + (amount * greenDetail), 0.0f, 1.0f);
            output.Pixels[i + 2] = Math.Clamp(source.Pixels[i + 2] + (amount * blueDetail), 0.0f, 1.0f);
        });

        return output;
    }

    private static RgbImageBuffer BoxBlur3x3(RgbImageBuffer source)
    {
        var output = new float[source.Pixels.Length];
        PixelParallel.ForRows(source.Height, y =>
        {
            for (var x = 0; x < source.Width; x++)
            {
                var sums = new float[3];
                var count = 0;
                for (var yy = Math.Max(0, y - 1); yy <= Math.Min(source.Height - 1, y + 1); yy++)
                {
                    for (var xx = Math.Max(0, x - 1); xx <= Math.Min(source.Width - 1, x + 1); xx++)
                    {
                        for (var c = 0; c < 3; c++)
                        {
                            sums[c] += source[xx, yy, c];
                        }

                        count++;
                    }
                }

                for (var c = 0; c < 3; c++)
                {
                    output[(((y * source.Width) + x) * 3) + c] = sums[c] / count;
                }
            }
        });

        return new RgbImageBuffer(source.Width, source.Height, output);
    }

    private static byte[] Ones(int length)
    {
        var values = new byte[length];
        Array.Fill(values, (byte)1);
        return values;
    }
}
