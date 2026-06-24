namespace Prokudin.Core.Color;

public sealed record LevelsSettings(
    LevelsMode Mode = LevelsMode.AutoPercentile,
    float BlackPoint = 0.0f,
    float WhitePoint = 1.0f,
    float Gamma = 1.0f,
    float AutoLowPercent = 1.0f,
    float AutoHighPercent = 99.0f,
    float AutoMaxGain = 1.3f);
