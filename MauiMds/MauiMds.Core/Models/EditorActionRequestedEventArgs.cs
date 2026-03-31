namespace MauiMds.Models;

public sealed class EditorActionRequestedEventArgs : EventArgs
{
    public required EditorActionType ActionType { get; init; }
}
