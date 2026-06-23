using OpenCvSharp;

namespace Prokudin.Core.Retouch;

internal static class HealingTileGroups
{
    public static IReadOnlyList<IReadOnlyList<int>> Build(IReadOnlyList<Mat> components, int width, int height)
    {
        var planner = PatchSearchPlanner.Build(width, height);
        var byTile = new Dictionary<int, List<int>>();

        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            var rect = HealingMaskUtils.BoundingRect(components[componentIndex]);
            var centerX = rect.X + (rect.Width / 2);
            var centerY = rect.Y + (rect.Height / 2);
            var tile = planner.GetTilesForRect(centerX, centerY, 1, 1).First();
            if (!byTile.TryGetValue(tile, out var group))
            {
                group = [];
                byTile[tile] = group;
            }

            group.Add(componentIndex);
        }

        return byTile.Values.Cast<IReadOnlyList<int>>().ToList();
    }
}
