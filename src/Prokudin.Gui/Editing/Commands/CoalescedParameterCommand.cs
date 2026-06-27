namespace Prokudin.Gui.Editing.Commands;

public sealed record CoalescedParameterCommand(
    EditorMemento BeforeState,
    string CoalesceKey,
    string Name) : IEditorCommand
{
    public const string ColorAdjustKey = "ColorAdjust";

    public static TimeSpan DefaultMergeWindow { get; } = TimeSpan.FromMilliseconds(700);

    public bool TryMergeWith(IEditorCommand other) =>
        other is CoalescedParameterCommand coalesced &&
        string.Equals(CoalesceKey, coalesced.CoalesceKey, StringComparison.Ordinal);
}
