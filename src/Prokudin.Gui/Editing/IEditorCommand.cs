namespace Prokudin.Gui.Editing;

public interface IEditorCommand
{
    string Name { get; }

    bool TryMergeWith(IEditorCommand other) => false;
}
