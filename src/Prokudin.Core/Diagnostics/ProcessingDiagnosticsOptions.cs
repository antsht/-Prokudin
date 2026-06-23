namespace Prokudin.Core.Diagnostics;

public sealed record ProcessingDiagnosticsOptions(
    ProcessingLogCategory EnabledCategories = ProcessingLogCategory.None,
    bool IncludeTimings = false)
{
    public bool IsEnabled(ProcessingLogCategory category) =>
        category != ProcessingLogCategory.None && (EnabledCategories & category) == category;
}
