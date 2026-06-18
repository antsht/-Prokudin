namespace Prokudin.Core.Retouch;

public sealed record CloneStampStroke(
    RetouchPoint SourceAnchor,
    RetouchStroke DestinationStroke,
    int BlendWidth,
    RetouchStroke? SourceMaskStroke = null);
