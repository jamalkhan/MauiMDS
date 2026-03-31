namespace MauiMds.Services;

public interface IMarkdownFileAccessService
{
    IDisposable? CreateAccessScope(string filePath);
    string? TryCreatePersistentAccessBookmark(string filePath);
    bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale);
}
