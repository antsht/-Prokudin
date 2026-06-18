namespace Prokudin.Core.Retouch;

public sealed record AutoCleanSettings(int Sensitivity = 50, int InpaintRadius = 3)
{
    public int NormalizedSensitivity => Math.Clamp(Sensitivity, 0, 100);

    public int NormalizedInpaintRadius => Math.Clamp(InpaintRadius, 1, 24);
}
