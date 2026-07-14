using Prokudin.Core.Imaging;

namespace Prokudin.Core.Retouch;

/// <summary>Aligned structural evidence for a Guided Healing request.</summary>
public sealed record HealingGuide(ImageBuffer Image, RetouchProvenanceMap Provenance)
{
    public HealingGuide Copy() => new(Image.Clone(), Provenance.Clone());
}
