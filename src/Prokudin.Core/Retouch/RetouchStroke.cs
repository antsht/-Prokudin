namespace Prokudin.Core.Retouch;

public sealed record RetouchStroke(IReadOnlyList<RetouchPoint> Points, int BrushSize);
