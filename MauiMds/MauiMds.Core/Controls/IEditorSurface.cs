namespace MauiMds.Controls;

public interface IEditorSurface
{
    void FocusEditor();
    void Undo();
    void Redo();
    Task CopySelectionAsync();
    Task CutSelectionAsync();
    Task PasteAsync();
    void ApplyParagraphStyle();
    void ApplyHeaderPrefix(int level);
    void ApplyBulletStyle();
    void ApplyChecklistStyle();
    void ApplyQuoteStyle();
    void ApplyCodeStyle();
    void ApplyBoldStyle();
    void ApplyItalicStyle();
    bool FindNext(string query);
}
