namespace Prokudin.Core.Crop;

public sealed record CropInfo(
    int X0,
    int Y0,
    int X1,
    int Y1,
    int OverlapX0,
    int OverlapY0,
    int OverlapX1,
    int OverlapY1);
