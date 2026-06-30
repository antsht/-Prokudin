using Prokudin.Gui.Editing.Commands;

namespace Prokudin.Gui.Editing;

public sealed class EditorHistory
{
    public const int DefaultLimit = 20;
    private const double DefaultBudgetFraction = 0.25;
    private const long MinimumByteBudget = 256L * 1024L * 1024L;
    private const long MaximumByteBudget = 4L * 1024L * 1024L * 1024L;

    private readonly int limit;
    private readonly long byteBudget;
    private readonly List<EditorMemento> undoStack = [];
    private readonly List<EditorMemento> redoStack = [];
    private long undoBytes;

    public EditorHistory(int limit = DefaultLimit, long? byteBudget = null)
    {
        this.limit = limit;
        this.byteBudget = byteBudget ?? CreateDefaultByteBudget();
    }

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    public EditorMementoKind? NextUndoKind => undoStack.Count > 0 ? undoStack[^1].Kind : null;

    public EditorMementoKind? NextRedoKind => redoStack.Count > 0 ? redoStack[^1].Kind : null;

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
        undoBytes += beforeState.ApproximateBytes;
        if (undoStack.Count > limit)
        {
            RemoveOldestUndo();
        }

        TrimUndoToByteBudget();
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
        undoBytes -= target.ApproximateBytes;
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
        undoBytes += currentStateForUndo.ApproximateBytes;
        TrimUndoToByteBudget();
        return target;
    }

    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
        undoBytes = 0;
    }

    private void TrimUndoToByteBudget()
    {
        while (undoStack.Count > 1 && undoBytes > byteBudget)
        {
            RemoveOldestUndo();
        }
    }

    private void RemoveOldestUndo()
    {
        var removed = undoStack[0];
        undoStack.RemoveAt(0);
        undoBytes -= removed.ApproximateBytes;
    }

    private static long CreateDefaultByteBudget()
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (available <= 0)
        {
            return MaximumByteBudget;
        }

        var budget = (long)(available * DefaultBudgetFraction);
        return Math.Clamp(budget, MinimumByteBudget, MaximumByteBudget);
    }
}
