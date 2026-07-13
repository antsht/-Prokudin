namespace Prokudin.Core.Color;

public sealed record WhitePickQualityEvaluation(
    WhitePickQualityIssue Issue,
    float MeanLuminance,
    float LuminanceStandardDeviation,
    float ChannelSpread)
{
    public bool HasWarning => Issue != WhitePickQualityIssue.None;

    public string? WarningMessage =>
        Issue switch
        {
            WhitePickQualityIssue.TooDark => "This White Pick sample is too dark to be reliable.",
            WhitePickQualityIssue.HighlyTextured => "This White Pick sample is highly textured and may be unreliable.",
            WhitePickQualityIssue.StronglyColored => "This White Pick sample is strongly coloured and may be unreliable.",
            _ => null,
        };
}
