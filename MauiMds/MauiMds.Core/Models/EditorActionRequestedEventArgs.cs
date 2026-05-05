using MauiMds.Controls;

namespace MauiMds.Models;

public sealed class EditorActionRequestedEventArgs : EventArgs
{
    public required Func<IEditorSurface, Task> Action { get; init; }
}
