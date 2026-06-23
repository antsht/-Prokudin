namespace Prokudin.Core.Retouch;

internal sealed class PatchSearchPlanner
{
    private readonly int tileSize;
    private readonly int tilesX;
    private readonly int tilesY;

    private PatchSearchPlanner(int width, int height, int tileSize)
    {
        this.tileSize = tileSize;
        tilesX = (width + tileSize - 1) / tileSize;
        tilesY = (height + tileSize - 1) / tileSize;
    }

    public int TileCount => tilesX * tilesY;

    public static PatchSearchPlanner Build(int width, int height, int tileSize = 256) =>
        new(width, height, tileSize);

    public IEnumerable<int> GetTilesForRect(int x, int y, int width, int height)
    {
        var x0 = Math.Clamp(x / tileSize, 0, tilesX - 1);
        var x1 = Math.Clamp((x + width - 1) / tileSize, 0, tilesX - 1);
        var y0 = Math.Clamp(y / tileSize, 0, tilesY - 1);
        var y1 = Math.Clamp((y + height - 1) / tileSize, 0, tilesY - 1);

        for (var ty = y0; ty <= y1; ty++)
        {
            for (var tx = x0; tx <= x1; tx++)
            {
                yield return (ty * tilesX) + tx;
            }
        }
    }
}
