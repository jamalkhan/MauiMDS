namespace MauiMds.Models;

public sealed class EditorDocumentState
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = "Untitled.mds";
    public string Content { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public bool IsUntitled { get; set; } = true;
    public bool IsDirty { get; set; }
    public bool IsReadOnly { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string EncodingName { get; set; } = "utf-8";
    public string NewLine { get; set; } = Environment.NewLine;
}
