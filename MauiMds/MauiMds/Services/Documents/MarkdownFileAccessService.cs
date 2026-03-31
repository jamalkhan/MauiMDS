using Microsoft.Extensions.Logging;
#if MACCATALYST
using Foundation;
#endif

namespace MauiMds.Services;

public sealed class MarkdownFileAccessService : IMarkdownFileAccessService
{
    private readonly ILogger<MarkdownFileAccessService> _logger;
#if MACCATALYST
    private readonly Dictionary<string, NSUrl> _securityScopedUrls = new(StringComparer.Ordinal);
#endif

    public MarkdownFileAccessService(ILogger<MarkdownFileAccessService> logger)
    {
        _logger = logger;
    }

    public IDisposable? CreateAccessScope(string filePath)
    {
#if MACCATALYST
        if (!_securityScopedUrls.TryGetValue(filePath, out var url))
        {
            return null;
        }

        return new SecurityScopedResourceAccess(url);
#else
        return null;
#endif
    }

    public string? TryCreatePersistentAccessBookmark(string filePath)
    {
#if MACCATALYST
        if (!_securityScopedUrls.TryGetValue(filePath, out var url))
        {
            return null;
        }

        var bookmarkData = url.CreateBookmarkData(
            NSUrlBookmarkCreationOptions.WithSecurityScope,
            [],
            null,
            out var error);

        if (error is not null || bookmarkData is null)
        {
            _logger.LogWarning("Failed to create persistent access bookmark for file {FilePath}. Error: {Error}", filePath, error?.LocalizedDescription);
            return null;
        }

        return Convert.ToBase64String(bookmarkData.ToArray());
#else
        return null;
#endif
    }

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
#if MACCATALYST
        restoredPath = null;
        isStale = false;

        if (string.IsNullOrWhiteSpace(bookmark))
        {
            return false;
        }

        try
        {
            var bookmarkBytes = Convert.FromBase64String(bookmark);
            using var bookmarkData = NSData.FromArray(bookmarkBytes);
            var resolvedUrl = NSUrl.FromBookmarkData(
                bookmarkData,
                NSUrlBookmarkResolutionOptions.WithSecurityScope,
                null,
                out isStale,
                out var error);

            if (error is not null || resolvedUrl is null)
            {
                _logger.LogWarning("Failed to restore persistent file access bookmark. Error: {Error}", error?.LocalizedDescription);
                return false;
            }

            restoredPath = resolvedUrl.Path;
            if (string.IsNullOrWhiteSpace(restoredPath))
            {
                return false;
            }

            TrackUrl(restoredPath, resolvedUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode or resolve persistent file access bookmark.");
            restoredPath = null;
            isStale = false;
            return false;
        }
#else
        restoredPath = null;
        isStale = false;
        return false;
#endif
    }

#if MACCATALYST
    internal void TrackUrl(string filePath, NSUrl url)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _securityScopedUrls[filePath] = url;
    }

    private sealed class SecurityScopedResourceAccess : IDisposable
    {
        private readonly NSUrl _url;
        private readonly bool _hasAccess;

        public SecurityScopedResourceAccess(NSUrl url)
        {
            _url = url;
            _hasAccess = _url.StartAccessingSecurityScopedResource();
        }

        public void Dispose()
        {
            if (_hasAccess)
            {
                _url.StopAccessingSecurityScopedResource();
            }
        }
    }
#endif
}
