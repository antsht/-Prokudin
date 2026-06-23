using System.Buffers;

namespace Prokudin.Core.Retouch;

internal static class HealingScratchBuffers
{
    public static float[] Rent(int pixelCount) => ArrayPool<float>.Shared.Rent(pixelCount);

    public static void Return(float[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}
