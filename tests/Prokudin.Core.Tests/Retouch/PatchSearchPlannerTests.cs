using FluentAssertions;
using Prokudin.Core.Retouch;

namespace Prokudin.Core.Tests.Retouch;

public sealed class PatchSearchPlannerTests
{
    [Fact]
    public void Build_MapsEveryComponentToAtLeastOneTile()
    {
        var planner = PatchSearchPlanner.Build(width: 300, height: 200, tileSize: 256);
        planner.TileCount.Should().BeGreaterThan(0);

        var tiles = new HashSet<int>();
        foreach (var tileIndex in planner.GetTilesForRect(x: 10, y: 10, width: 40, height: 40))
        {
            tiles.Add(tileIndex);
        }

        tiles.Should().NotBeEmpty();
    }
}
