namespace Prokudin.Core.Color;

public sealed record ColorSettings(
    bool AutoWhiteBalance = true,
    int Temperature = 0,
    int Tint = 0,
    bool PipetteActive = false,
    int PipetteX = -1,
    int PipetteY = -1,
    int PipetteRadius = 3);
