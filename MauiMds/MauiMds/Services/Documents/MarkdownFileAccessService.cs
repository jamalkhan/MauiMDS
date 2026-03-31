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
            _logger.LogTrace("No tracked security-scoped URL found for {FilePath}.", filePath);
            return null;
        }

        var access = new SecurityScopedResourceAccess(url);
        _logger.LogTrace(
            "Created security-scoped access scope. FilePath: {FilePath}, Granted: {Granted}",
            filePath,
            access.HasAccess);
        return access;
#else
        return null;
#endif
    }

    public string? TryCreatePersistentAccessBookmark(string filePath)
    {
#if MACCATALYST
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (!_securityScopedUrls.TryGetValue(filePath, out var url))
        {
            _logger.LogDebug(
                "Skipping persistent access bookmark for {FilePath} because no security-scoped URL is tracked for it yet.",
                filePath);
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

        _logger.LogTrace("Created persistent access bookmark for {FilePath}.", filePath);
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
            _logger.LogTrace(
                "Restored persistent access bookmark. FilePath: {FilePath}, IsStale: {IsStale}",
                restoredPath,
                isStale);
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

    public bool TryValidateReadAccess(string filePath)
    {
#if MACCATALYST
        using var access = CreateAccessScope(filePath);
        try
        {
            using var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _logger.LogTrace("Validated read access for {FilePath}.", filePath);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Read access validation failed for {FilePath}.", filePath);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Read access validation could not open {FilePath}.", filePath);
            return false;
        }
#else
        return File.Exists(filePath);
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
        _logger.LogTrace("Tracked security-scoped URL. FilePath: {FilePath}", filePath);
    }

    private sealed class SecurityScopedResourceAccess : IDisposable
    {
        private readonly NSUrl _url;
        public bool HasAccess { get; }

        public SecurityScopedResourceAccess(NSUrl url)
        {
            _url = url;
            HasAccess = _url.StartAccessingSecurityScopedResource();
        }

        public void Dispose()
        {
            if (HasAccess)
            {
                _url.StopAccessingSecurityScopedResource();
            }
        }
    }
#endif
}
