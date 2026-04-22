namespace MauiMds.Models;

public sealed class KeyboardShortcutDefinition
{
    public EditorActionType Action { get; set; }
    public string Key { get; set; } = string.Empty;
}
