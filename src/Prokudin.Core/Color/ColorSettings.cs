namespace Prokudin.Core.Color;

public sealed record ColorSettings(
    WhiteBalanceSource Source = WhiteBalanceSource.Auto,
    int Temperature = 0,
    int Tint = 0,
    WhitePick? WhitePick = null)
{
    public ColorSettings(
        bool AutoWhiteBalance,
        int Temperature = 0,
        int Tint = 0,
        bool PipetteActive = false,
        int PipetteX = -1,
        int PipetteY = -1,
        int PipetteRadius = 3)
        : this(
            PipetteActive && PipetteX >= 0 && PipetteY >= 0
                ? WhiteBalanceSource.WhitePick
                : AutoWhiteBalance ? WhiteBalanceSource.Auto : WhiteBalanceSource.Off,
            Temperature,
            Tint,
            PipetteActive && PipetteX >= 0 && PipetteY >= 0
                ? new WhitePick(PipetteX, PipetteY, PipetteRadius)
                : null)
    {
    }
}
