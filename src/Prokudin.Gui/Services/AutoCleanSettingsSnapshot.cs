using Prokudin.Core.Retouch;

namespace Prokudin.Gui.Services;

public sealed record AutoCleanSettingsSnapshot(
    AutoCleanQualityMode QualityMode = AutoCleanQualityMode.Quality);
