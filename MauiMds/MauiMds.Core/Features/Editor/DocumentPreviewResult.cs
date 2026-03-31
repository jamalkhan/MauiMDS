using MauiMds.Models;

namespace MauiMds.Features.Editor;

public sealed class DocumentPreviewResult
{
    public IReadOnlyList<MarkdownBlock> Blocks { get; init; } = Array.Empty<MarkdownBlock>();
    public EditorViewMode ViewMode { get; init; }
    public string? InlineErrorMessage { get; init; }
}
