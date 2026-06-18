using Prokudin.Core.Imaging;

namespace Prokudin.Core.Alignment;

public sealed record AlignOptions(
    ChannelName Reference = ChannelName.Green,
    string Detector = "sift",
    int MaxFineIterations = 3,
    bool TrimBorders = true,
    int MaxTranslation = 48);
