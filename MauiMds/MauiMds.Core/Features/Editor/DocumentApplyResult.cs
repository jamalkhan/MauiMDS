using MauiMds.Models;

namespace MauiMds.Features.Editor;

public sealed class DocumentApplyResult
{
    public required EditorDocumentState DocumentState { get; init; }
    public required bool FilePathChanged { get; init; }
    public required bool FileNameChanged { get; init; }
    public required bool IsDirtyChanged { get; init; }
    public required bool IsUntitledChanged { get; init; }
    public required bool ShouldWatchDocument { get; init; }
    public string? WatchFilePath { get; init; }
}
