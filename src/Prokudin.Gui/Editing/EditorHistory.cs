using Prokudin.Gui.Editing.Commands;

namespace Prokudin.Gui.Editing;

public sealed class EditorHistory
{
    public const int DefaultLimit = 20;

    private readonly int limit;
    private readonly List<EditorMemento> undoStack = [];
    private readonly List<EditorMemento> redoStack = [];

    public EditorHistory(int limit = DefaultLimit)
    {
        this.limit = limit;
    }

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    public void Record(IEditorCommand command)
    {
        switch (command)
        {
            case SnapshotCommand snapshot:
                RecordSnapshot(snapshot.BeforeState);
                return;
            case CoalescedParameterCommand coalesced:
                RecordSnapshot(coalesced.BeforeState);
                return;
            default:
                throw new NotSupportedException($"Unsupported editor command type: {command.GetType().Name}.");
        }
    }

    public void RecordSnapshot(EditorMemento beforeState)
    {
        undoStack.Add(beforeState);
        if (undoStack.Count > limit)
        {
            undoStack.RemoveAt(0);
        }

        redoStack.Clear();
    }

    public EditorMemento? TakeUndoTarget(EditorMemento currentStateForRedo)
    {
        if (undoStack.Count == 0)
        {
            return null;
        }

        var target = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        redoStack.Add(currentStateForRedo);
        return target;
    }

    public EditorMemento? TakeRedoTarget(EditorMemento currentStateForUndo)
    {
        if (redoStack.Count == 0)
        {
            return null;
        }

        var target = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        undoStack.Add(currentStateForUndo);
        return target;
    }

    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
    }
}
