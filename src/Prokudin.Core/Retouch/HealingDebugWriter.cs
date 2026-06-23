using OpenCvSharp;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

internal static class HealingDebugWriter
{
    private static string? _sessionDirectory;

    public static void SaveComponentDebug(
        HealOptions options,
        ImageBuffer original,
        Mat componentMask,
        float[] prediction,
        float[] patch,
        float[] final,
        float confidence)
    {
        var directory = EnsureSessionDirectory(options);
        SaveNormalized(Path.Combine(directory, $"prediction_confidence_{confidence:F2}.png"), original.Width, original.Height, BuildConfidenceMap(original.PixelCount, confidence));
        SaveNormalized(Path.Combine(directory, "prediction_channel.png"), original.Width, original.Height, prediction);
        SaveNormalized(Path.Combine(directory, "patch_heal_channel.png"), original.Width, original.Height, patch);
        SaveNormalized(Path.Combine(directory, "final_healed_channel.png"), original.Width, original.Height, final);
        Cv2.ImWrite(Path.Combine(directory, "component_debug.png"), componentMask);
        _ = original;
    }

    public static void SaveFinalDebug(
        HealOptions options,
        ImageBuffer original,
        ImageBuffer healed,
        byte[] mask,
        int width,
        int height)
    {
        var directory = EnsureSessionDirectory(options);
        using var maskMat = HealingMaskUtils.MaskToMat(mask, width, height);
        Cv2.ImWrite(Path.Combine(directory, "mask_target.png"), maskMat);
        SaveBuffer(Path.Combine(directory, "final_healed_channel.png"), healed);
        SaveBuffer(Path.Combine(directory, "original_channel.png"), original);
    }

    public static void SaveAutoCleanMaskDebug(
        AutoCleanSettings settings,
        AutoCleanMaskResult result,
        int width,
        int height)
    {
        var directory = string.IsNullOrWhiteSpace(settings.DebugOutputDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), "debug", "auto-clean", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
            : settings.DebugOutputDirectory;
        Directory.CreateDirectory(directory);

        var prefix = settings.DebugMaskPrefix ?? string.Empty;
        SaveMask(Path.Combine(directory, $"{prefix}auto_defect_mask_raw.png"), result.RawMask, width, height);
        SaveMask(Path.Combine(directory, $"{prefix}auto_defect_mask_merged.png"), result.MergedMask, width, height);
        SaveMask(Path.Combine(directory, $"{prefix}auto_defect_mask_expanded.png"), result.ExpandedMask, width, height);
        SaveMask(Path.Combine(directory, $"{prefix}final_healing_mask.png"), result.FinalMask, width, height);
    }

    private static string EnsureSessionDirectory(HealOptions options)
    {
        if (_sessionDirectory is not null && Directory.Exists(_sessionDirectory))
        {
            return _sessionDirectory;
        }

        var root = string.IsNullOrWhiteSpace(options.DebugOutputDirectory)
            ? Path.Combine(Directory.GetCurrentDirectory(), "debug", "heal")
            : options.DebugOutputDirectory;
        _sessionDirectory = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(_sessionDirectory);
        return _sessionDirectory;
    }

    private static void SaveNormalized(string path, int width, int height, float[] values)
    {
        using var mat = new Mat(height, width, MatType.CV_32FC1);
        System.Runtime.InteropServices.Marshal.Copy(values, 0, mat.Data, values.Length);
        using var u8 = new Mat();
        mat.ConvertTo(u8, MatType.CV_8UC1, 255.0);
        Cv2.ImWrite(path, u8);
    }

    private static void SaveBuffer(string path, ImageBuffer image)
    {
        using var mat = ImageMatConverter.ToNormalizedFloatMat(image);
        using var u8 = new Mat();
        mat.ConvertTo(u8, MatType.CV_8UC1, 255.0);
        Cv2.ImWrite(path, u8);
    }

    private static void SaveMask(string path, byte[] mask, int width, int height)
    {
        using var maskMat = HealingMaskUtils.MaskToMat(mask, width, height);
        Cv2.ImWrite(path, maskMat);
    }

    private static float[] BuildConfidenceMap(int pixelCount, float confidence)
    {
        var map = new float[pixelCount];
        Array.Fill(map, confidence);
        return map;
    }
}
