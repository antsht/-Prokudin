using Prokudin.Core.Imaging;

namespace Prokudin.Core.Alignment;

public sealed record AlignResult(
    ImageBuffer Image,
    byte[] Mask,
    string TransformKind,
    int InlierCount,
    IReadOnlyList<(float Dx, float Dy)> SubpixelShifts);
