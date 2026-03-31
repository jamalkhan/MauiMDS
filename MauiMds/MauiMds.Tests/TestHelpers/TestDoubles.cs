using MauiMds.Models;
using MauiMds.Services;

namespace MauiMds.Tests.TestHelpers;

internal sealed class FakeWorkspaceBrowserService : IWorkspaceBrowserService
{
    public string? BookmarkToReturn { get; set; }
    public bool RestoreBookmarkResult { get; set; }
    public string? RestoredPath { get; set; }
    public bool RestoredBookmarkIsStale { get; set; }

    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<WorkspaceNodeInfo>> LoadWorkspaceTreeAsync(string rootPath, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WorkspaceNodeInfo>>([]);

    public Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> FileContainsTextAsync(string filePath, string query, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<string> CreateMarkdownSharpFileAsync(string directoryPath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> RenameMarkdownFileAsync(string filePath, string newFileName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public string? TryCreatePersistentAccessBookmark(string folderPath) => BookmarkToReturn;

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
        restoredPath = RestoredPath;
        isStale = RestoredBookmarkIsStale;
        return RestoreBookmarkResult;
    }
}

internal sealed class FakeMarkdownDocumentService : IMarkdownDocumentService
{
    public string? BookmarkToReturn { get; set; }
    public bool RestoreBookmarkResult { get; set; }
    public string? RestoredPath { get; set; }
    public bool RestoredBookmarkIsStale { get; set; }

    public Task<MarkdownDocument?> LoadInitialDocumentAsync() => throw new NotSupportedException();
    public Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string?> PickDocumentPathAsync() => throw new NotSupportedException();
    public Task<MarkdownDocument?> PickDocumentAsync() => throw new NotSupportedException();
    public Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null) => throw new NotSupportedException();
    public Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public string? TryCreatePersistentAccessBookmark(string filePath) => BookmarkToReturn;

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
        restoredPath = RestoredPath;
        isStale = RestoredBookmarkIsStale;
        return RestoreBookmarkResult;
    }
}

internal sealed class FakeSessionStateService : ISessionStateService
{
    public SessionState LoadedState { get; set; } = new();
    public SessionState? SavedState { get; private set; }

    public SessionState Load() => LoadedState;

    public void Save(SessionState state)
    {
        SavedState = state;
    }
}
