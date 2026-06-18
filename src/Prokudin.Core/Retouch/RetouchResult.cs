using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

public sealed record RetouchResult(ImageBuffer Image, byte[] Mask);
