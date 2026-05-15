using Rizedown.Controls;

namespace Rizedown.Models;

public sealed class EditorActionRequestedEventArgs : EventArgs
{
    public required Func<IEditorSurface, Task> Action { get; init; }
}
