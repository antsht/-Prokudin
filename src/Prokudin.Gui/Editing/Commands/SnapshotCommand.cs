namespace Prokudin.Gui.Editing.Commands;

public sealed record SnapshotCommand(EditorMemento BeforeState, string Name) : IEditorCommand;
