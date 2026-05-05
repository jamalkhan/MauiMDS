namespace MauiMds.Services;

public sealed class MarkdownFileAccessService : IMarkdownFileAccessService
{
    public IDisposable? CreateAccessScope(string filePath) => null;

    public string? TryCreatePersistentAccessBookmark(string filePath) => null;

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
        restoredPath = null;
        isStale = false;
        return false;
    }

    public bool TryValidateReadAccess(string filePath) => File.Exists(filePath);
}
