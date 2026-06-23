namespace Prokudin.Core.Retouch;

public sealed class AutoCleanSessionCache
{
    private float[]? target;
    private float[]? guide1;
    private float[]? guide2;

    public void Store(float[] target, float[] guide1, float[] guide2)
    {
        this.target = (float[])target.Clone();
        this.guide1 = (float[])guide1.Clone();
        this.guide2 = (float[])guide2.Clone();
    }

    public bool TryGet(out float[] target, out float[] guide1, out float[] guide2)
    {
        if (this.target is null || this.guide1 is null || this.guide2 is null)
        {
            target = [];
            guide1 = [];
            guide2 = [];
            return false;
        }

        target = this.target;
        guide1 = this.guide1;
        guide2 = this.guide2;
        return true;
    }

    public void Clear()
    {
        target = null;
        guide1 = null;
        guide2 = null;
    }
}
