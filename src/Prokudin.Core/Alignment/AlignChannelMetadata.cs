using Prokudin.Core.Imaging;

namespace Prokudin.Core.Alignment;

public sealed record AlignChannelMetadata(
    string Kind,
    int Inliers,
    IReadOnlyList<(float Dx, float Dy)>? Shifts = null)
{
    public static string FormatStatus(IReadOnlyDictionary<ChannelName, AlignChannelMetadata> metadata)
    {
        var parts = new List<string>();
        foreach (var name in new[] { ChannelName.Red, ChannelName.Green, ChannelName.Blue })
        {
            if (!metadata.TryGetValue(name, out var info))
            {
                continue;
            }

            var label = name.ToString();
            if (info.Shifts is null or { Count: 0 })
            {
                parts.Add($"{label}: {info.Kind} ({info.Inliers} inliers)");
                continue;
            }

            var shiftText = string.Join(
                ", ",
                info.Shifts.Select(shift => $"({shift.Dx:F1},{shift.Dy:F1})"));
            parts.Add($"{label}: {info.Kind} ({info.Inliers} inliers, shifts {shiftText})");
        }

        return string.Join("; ", parts);
    }
}
