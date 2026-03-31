using MauiMds.Models;

namespace MauiMds.Features.Editor;

public sealed class DocumentLoadResult
{
    public required EditorDocumentState DocumentState { get; init; }
    public required IReadOnlyList<MarkdownBlock> Blocks { get; init; }
    public required EditorViewMode ViewMode { get; init; }
    public string? InlineErrorMessage { get; init; }
}
