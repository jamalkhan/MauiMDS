namespace MauiMds.Models;

public sealed class MarkdownDocument
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public string? FileName { get; init; }
    public long? FileSizeBytes { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}
