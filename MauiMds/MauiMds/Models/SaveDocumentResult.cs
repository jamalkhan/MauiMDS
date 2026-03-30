namespace MauiMds.Models;

public sealed class SaveDocumentResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTimeOffset LastModified { get; init; }
}
