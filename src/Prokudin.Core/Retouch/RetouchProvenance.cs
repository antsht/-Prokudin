namespace Prokudin.Core.Retouch;

/// <summary>
/// Records the trust level of a working-channel pixel when it is considered as
/// structural evidence by a later Guided Healing operation.
/// </summary>
public enum RetouchProvenance : byte
{
    Original = 0,
    HighConfidenceHealing = 1,
    LowConfidenceHealing = 2,
    CloneStamp = 3,
    Unknown = 4,
}
