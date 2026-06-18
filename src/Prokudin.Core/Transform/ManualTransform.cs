namespace Prokudin.Core.Transform;

public readonly record struct ManualTransform(float Dx = 0.0f, float Dy = 0.0f, float AngleDegrees = 0.0f)
{
    public bool IsIdentity => Math.Abs(Dx) < 1e-6f && Math.Abs(Dy) < 1e-6f && Math.Abs(AngleDegrees) < 1e-6f;
}
