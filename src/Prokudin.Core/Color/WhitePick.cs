namespace Prokudin.Core.Color;

public sealed record WhitePick(int X, int Y, int Radius = 3)
{
    public int EffectiveRadius => Math.Clamp(Radius, 1, 25);
}
