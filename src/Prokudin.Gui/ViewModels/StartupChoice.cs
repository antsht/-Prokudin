namespace Prokudin.Gui.ViewModels;

public enum StartupChoiceType
{
    NewProject,
    RecoverAutosave,
    OpenRecent,
    OpenOther,
}

public sealed class StartupChoice
{
    public required StartupChoiceType Type { get; init; }

    public string? ProjectPath { get; init; }
}

public enum UnsavedChangesResult
{
    Save,
    DontSave,
    Cancel,
}
