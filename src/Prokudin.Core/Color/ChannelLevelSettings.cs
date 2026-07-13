namespace Prokudin.Core.Color;

public sealed record ChannelLevelSettings(
    float BlackPoint = 0.0f,
    float WhitePoint = 1.0f,
    float Gamma = 1.0f)
{
    public bool IsNeutral =>
        Math.Abs(BlackPoint) < 1e-6f &&
        Math.Abs(WhitePoint - 1.0f) < 1e-6f &&
        Math.Abs(Gamma - 1.0f) < 1e-6f;
}
