namespace MauiMds.Controls;

public interface IEditorSurface
{
    void FocusEditor();
    void Undo();
    void Redo();
    Task CopySelectionAsync();
    Task CutSelectionAsync();
    Task PasteAsync();
    void ApplyHeaderPrefix(int level);
    bool FindNext(string query);
}
