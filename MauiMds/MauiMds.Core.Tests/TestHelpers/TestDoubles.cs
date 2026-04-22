using MauiMds.Models;
using MauiMds.Services;

namespace MauiMds.Core.Tests.TestHelpers;

internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset? initialUtcNow = null)
    {
        UtcNow = initialUtcNow ?? new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; private set; }
    public DateTimeOffset Now => UtcNow.ToLocalTime();

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "Fake time cannot move backwards.");
        }

        UtcNow = UtcNow.Add(by);
    }
}

internal sealed class FakeDelayScheduler : IDelayScheduler
{
    private readonly FakeClock _clock;
    private readonly List<ScheduledDelay> _scheduledDelays = [];

    public FakeDelayScheduler(FakeClock clock)
    {
        _clock = clock;
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (delay <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var scheduledDelay = new ScheduledDelay(_clock.UtcNow.Add(delay), cancellationToken);
        _scheduledDelays.Add(scheduledDelay);
        return scheduledDelay.Task;
    }

    public void AdvanceBy(TimeSpan by)
    {
        _clock.Advance(by);
        CompleteDueDelays();
    }

    private void CompleteDueDelays()
    {
        for (var i = _scheduledDelays.Count - 1; i >= 0; i--)
        {
            var scheduledDelay = _scheduledDelays[i];
            if (scheduledDelay.CancellationToken.IsCancellationRequested)
            {
                scheduledDelay.TrySetCanceled();
                _scheduledDelays.RemoveAt(i);
                continue;
            }

            if (scheduledDelay.DueUtc <= _clock.UtcNow)
            {
                scheduledDelay.TrySetResult();
                _scheduledDelays.RemoveAt(i);
            }
        }
    }

    private sealed class ScheduledDelay
    {
        private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _cancellationRegistration;

        public ScheduledDelay(DateTimeOffset dueUtc, CancellationToken cancellationToken)
        {
            DueUtc = dueUtc;
            CancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(() => _completionSource.TrySetCanceled(cancellationToken));
        }

        public DateTimeOffset DueUtc { get; }
        public CancellationToken CancellationToken { get; }
        public Task Task => _completionSource.Task;

        public void TrySetResult()
        {
            _cancellationRegistration.Dispose();
            _completionSource.TrySetResult();
        }

        public void TrySetCanceled()
        {
            _cancellationRegistration.Dispose();
            _completionSource.TrySetCanceled(CancellationToken);
        }
    }
}

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
