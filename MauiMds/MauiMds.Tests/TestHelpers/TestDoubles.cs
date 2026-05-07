using MauiMds;
using MauiMds.AudioCapture;
using MauiMds.Features.Editor;
using MauiMds.Features.Export;
using MauiMds.Features.Session;
using MauiMds.Features.Workspace;
using MauiMds.Logging;
using MauiMds.Models;
using MauiMds.Services;
using MauiMds.Transcription;
using Microsoft.Extensions.Logging;

namespace MauiMds.Tests.TestHelpers;

/// <summary>
/// Executes all dispatcher calls synchronously inline. Convenient for most tests, but
/// masks bugs where code incorrectly assumes BeginInvokeOnMainThread is deferred.
/// Use <see cref="FakeQueuedDispatcher"/> when you need to verify that work is
/// properly posted to the main thread rather than executing inline.
/// </summary>
internal sealed class FakeSynchronousDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) => action();
    public Task InvokeOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
    public Task InvokeOnMainThreadAsync(Func<Task> action) => action();
}

/// <summary>
/// Queues BeginInvokeOnMainThread actions rather than executing them immediately.
/// Call <see cref="Flush"/> to run queued actions. InvokeOnMainThreadAsync runs
/// synchronously (it must complete before the awaiting caller continues).
/// </summary>
internal sealed class FakeQueuedDispatcher : IMainThreadDispatcher
{
    private readonly Queue<Action> _queue = new();

    public int QueuedCount => _queue.Count;

    public void BeginInvokeOnMainThread(Action action) => _queue.Enqueue(action);

    public Task InvokeOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
    public Task InvokeOnMainThreadAsync(Func<Task> action) => action();

    public void Flush()
    {
        while (_queue.Count > 0)
            _queue.Dequeue()();
    }
}

internal sealed class FakeApplicationLifetime : IApplicationLifetime
{
    public bool IsTerminating { get; set; }
}

internal sealed class FakeAlertService : IAlertService
{
    public List<(string Title, string Message, string Cancel)> Calls { get; } = [];
    public Task ShowAlertAsync(string title, string message, string cancel)
    {
        Calls.Add((title, message, cancel));
        return Task.CompletedTask;
    }
}

internal sealed class FakeAutosaveCoordinator : IAutosaveCoordinator
{
    public record ScheduleCall(bool IsEnabled, bool IsUntitled, bool IsDirty, string? FilePath, TimeSpan Delay);

    public List<ScheduleCall> Calls { get; } = [];
    public int CancelCount { get; private set; }

    public void Schedule(bool isEnabled, bool isUntitled, bool isDirty, string? filePath, TimeSpan delay, Func<Task> saveAction)
        => Calls.Add(new ScheduleCall(isEnabled, isUntitled, isDirty, filePath, delay));

    public void Cancel() => CancelCount++;
    public void Dispose() => Cancel();
}

internal sealed class FakeDocumentApplyService : IDocumentApplyService
{
    private readonly IDocumentWorkflowService _workflow;
    public FakeDocumentApplyService(IDocumentWorkflowService workflow) => _workflow = workflow;

    public DocumentApplyResult PrepareApply(EditorDocumentState currentState, MarkdownDocument document)
    {
        var next = _workflow.CreateDocumentState(document);
        return new DocumentApplyResult
        {
            DocumentState = next,
            FilePathChanged = !string.Equals(currentState.FilePath, next.FilePath, StringComparison.Ordinal),
            FileNameChanged = !string.Equals(currentState.FileName, next.FileName, StringComparison.Ordinal),
            IsDirtyChanged = currentState.IsDirty != next.IsDirty,
            IsUntitledChanged = currentState.IsUntitled != next.IsUntitled,
            ShouldWatchDocument = !next.IsUntitled,
            WatchFilePath = !next.IsUntitled ? next.FilePath : null
        };
    }
}

internal sealed class FakePlatformInfo : IPlatformInfo
{
    public bool IsMacCatalyst { get; set; }
    public bool IsWindows { get; set; }
}

internal sealed class FakeEditorPreferencesService : IEditorPreferencesService
{
    private EditorPreferences _prefs = new();
    public EditorPreferences Load() => _prefs;
    public void Save(EditorPreferences preferences) => _prefs = preferences;
}

internal sealed class FakeMarkdownDocumentService : IMarkdownDocumentService
{
    public string? BookmarkToReturn { get; set; }
    public bool RestoreBookmarkResult { get; set; }
    public string? RestoredPath { get; set; }
    public bool RestoredBookmarkIsStale { get; set; }

    public MarkdownDocument? DocumentToLoad { get; set; }
    public SaveDocumentResult SaveResult { get; set; } = new()
    {
        FilePath = string.Empty, FileName = string.Empty, FileSizeBytes = 0,
        LastModified = DateTimeOffset.UtcNow
    };

    public Task<MarkdownDocument?> LoadInitialDocumentAsync() => Task.FromResult(DocumentToLoad);
    public Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult(DocumentToLoad ?? new MarkdownDocument { FilePath = filePath, Content = string.Empty });
    public Task<string?> PickDocumentPathAsync() => Task.FromResult<string?>(null);
    public Task<MarkdownDocument?> PickDocumentAsync() => Task.FromResult<MarkdownDocument?>(null);
    public Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null)
        => Task.FromResult(new MarkdownDocument { FilePath = string.Empty, Content = string.Empty, IsUntitled = true, FileName = suggestedName ?? "Untitled" });
    public Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
        => Task.FromResult(SaveResult);
    public Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
        => Task.FromResult<SaveDocumentResult?>(null);
    public string? TryCreatePersistentAccessBookmark(string filePath) => BookmarkToReturn;
    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
        restoredPath = RestoredPath;
        isStale = RestoredBookmarkIsStale;
        return RestoreBookmarkResult;
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

    public Task RenameRecordingGroupAsync(RecordingGroup group, string newBaseName)
        => throw new NotSupportedException();

    public string? TryCreatePersistentAccessBookmark(string folderPath) => BookmarkToReturn;

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
    public void Save(SessionState state) => SavedState = state;
}

internal sealed class FakeDocumentWatchService : IDocumentWatchService
{
    public event EventHandler<string>? DocumentChanged;
    public void Watch(string? filePath) { }
    public void Stop() { }
    public void Dispose() { }
    public void RaiseDocumentChanged(string path) => DocumentChanged?.Invoke(this, path);
}

internal sealed class FakeSessionRestoreCoordinator : ISessionRestoreCoordinator
{
    public SessionState StateToLoad { get; set; } = new();
    public SessionPersistenceRequest? LastSaved { get; private set; }

    public SessionState Load() => StateToLoad;
    public void Save(SessionPersistenceRequest request) => LastSaved = request;
    public string? ResolveWorkspaceRestorePath(SessionState sessionState, out string? repickMessage)
    {
        repickMessage = null;
        return null;
    }
    public string? ResolveDocumentRestorePath(SessionState sessionState, bool hasWorkspaceAccess, out bool needsRepick)
    {
        needsRepick = false;
        return null;
    }
}

internal sealed class FakePreviewPipelineCoordinator : IPreviewPipelineCoordinator
{
    public void MarkSaved() { }
    public bool ShouldSuppressExternalReload(bool isSavingDocument, TimeSpan suppressionWindow) => false;
    public Task SchedulePreviewAsync(MarkdownDocument snapshot, EditorViewMode currentViewMode, TimeSpan delay,
        Func<MarkdownDocument, DocumentPreviewResult, TimeSpan, Task> applyPreviewAsync) => Task.CompletedTask;
    public Task ScheduleExternalReloadAsync(TimeSpan delay, Func<Task> reloadAsync) => Task.CompletedTask;
    public void CancelPreview() { }
    public void CancelExternalReload() { }
    public void Dispose() { }
}

internal sealed class FakePdfExportService : IPdfExportService
{
    public bool ReturnValue { get; set; } = true;
    public Task<bool> ExportAsync(IReadOnlyList<MarkdownBlock> blocks, string suggestedFileName, CancellationToken cancellationToken = default)
        => Task.FromResult(ReturnValue);
}

internal sealed class FakeEditorModeSupportService : IEditorModeSupportService
{
    public bool IsVisualEditorSupported { get; set; } = true;
    public string VisualEditorUnavailableMessage { get; set; } = string.Empty;
    public EditorViewMode ResolveSupportedViewMode(EditorViewMode requestedMode, bool showUnsupportedSnackbar) => requestedMode;
}

internal sealed class FakeAudioCaptureService : IAudioCaptureService
{
    public AudioCaptureState State { get; private set; } = AudioCaptureState.Idle;
    public string? LastStartWarning { get; set; }
    public AudioPermissionStatus PermissionToReturn { get; set; } = AudioPermissionStatus.Granted;
    public AudioPermissionStatus? RequestPermissionToReturn { get; set; }
    public AudioCaptureResult ResultToReturn { get; set; } = new() { Success = true };

    /// <summary>
    /// When set, <see cref="StartAsync"/> throws this exception instead of starting.
    /// State remains <see cref="AudioCaptureState.Idle"/> and <see cref="StateChanged"/>
    /// is not fired — mirrors how the real service behaves on a failed start.
    /// </summary>
    public Exception? StartException { get; set; }

    public event EventHandler<AudioCaptureState>? StateChanged;
    public event EventHandler<LiveAudioChunk>? LiveChunkAvailable;

    public Task<AudioPermissionStatus> CheckMicrophonePermissionAsync() => Task.FromResult(PermissionToReturn);
    public Task<AudioPermissionStatus> RequestMicrophonePermissionAsync()
        => Task.FromResult(RequestPermissionToReturn ?? PermissionToReturn);

    public Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
        if (StartException is not null)
            throw StartException;
        State = AudioCaptureState.Recording;
        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync()
    {
        State = AudioCaptureState.Idle;
        StateChanged?.Invoke(this, State);
        return Task.FromResult(ResultToReturn);
    }
}

internal sealed class FakeAudioPlayerService : IAudioPlayerService
{
    public string? CurrentlyPlayingPath { get; private set; }
    public bool IsPlaying { get; private set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? PlaybackPositionChanged;

    public Task PlayAsync(string filePath)
    {
        CurrentlyPlayingPath = filePath;
        IsPlaying = true;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Pause() { IsPlaying = false; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); }
    public void Stop() { IsPlaying = false; CurrentlyPlayingPath = null; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); }
    public void Seek(TimeSpan position) { Position = position; PlaybackPositionChanged?.Invoke(this, EventArgs.Empty); }
}

internal sealed class FakeTranscriptionPipelineFactory : ITranscriptionPipelineFactory
{
    public IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines { get; } = [];
    public IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines { get; } = [];
    public ILiveTranscriptionSession? SessionToReturn { get; set; }
    public ITranscriptionPipeline? PipelineToReturn { get; set; }

    public ITranscriptionPipeline Create(
        TranscriptionEngineType engine, DiarizationEngineType diarization,
        string whisperBinaryPath = "", string whisperModelPath = "",
        string pyannotePythonPath = "", string pyannoteHfToken = "")
        => PipelineToReturn ?? throw new NotSupportedException("Transcription not expected in unit tests.");

    public ILiveTranscriptionSession? CreateLiveSession(
        TranscriptionEngineType engine,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        INativeMicrophoneSource? nativeMicSource = null)
        => SessionToReturn;
}

internal sealed class FakeLiveTranscriptionSession : ILiveTranscriptionSession
{
    public event EventHandler<IReadOnlyList<TranscriptSegment>>? SegmentsReady;

    public int FeedChunkCallCount { get; private set; }
    public bool FlushCalled { get; private set; }
    public bool IsDisposed { get; private set; }
    public List<string> FedChunkPaths { get; } = [];

    public Task FeedChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct = default)
    {
        FeedChunkCallCount++;
        FedChunkPaths.Add(wavChunkPath);
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default) { FlushCalled = true; return Task.CompletedTask; }

    public ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }

    public void RaiseSegmentsReady(IReadOnlyList<TranscriptSegment> segments)
        => SegmentsReady?.Invoke(this, segments);
}

internal sealed class FakeTranscriptionPipeline : ITranscriptionPipeline
{
    public string TranscriptionEngineName { get; set; } = "Fake";
    public string DiarizationEngineName { get; set; } = "None";
    public TranscriptDocument DocumentToReturn { get; set; } = new();
    public int RunCallCount { get; private set; }

    public Task<TranscriptDocument> RunAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RunCallCount++;
        return Task.FromResult(DocumentToReturn);
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Now => UtcNow.ToLocalTime();
}

internal sealed class FakeTranscriptStorage : ITranscriptStorage
{
    public List<(string Path, string Content)> Writes { get; } = [];
    public List<(string Source, string Dest)> Moves { get; } = [];

    public string GetTranscriptPath(RecordingGroup group)
        => Path.Combine(group.DirectoryPath, group.BaseName + "_transcript.md");

    public string GetRotatedPath(string existingPath)
    {
        var dir  = Path.GetDirectoryName(existingPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(existingPath);
        var ext  = Path.GetExtension(existingPath);
        return Path.Combine(dir, $"{stem}.old{ext}");
    }

    public Task WriteAsync(string path, string content)
    {
        Writes.Add((path, content));
        return Task.CompletedTask;
    }

    public bool Exists(string path) => Writes.Any(w => w.Path == path);

    public void Move(string sourcePath, string destPath) => Moves.Add((sourcePath, destPath));
}

internal sealed class FakeTranscriptFormatter : ITranscriptFormatter
{
    public string FormatLiveProgress(RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments)
        => $"[live-progress:{group.DisplayName}:{string.Join(",", segments.Select(s => s.Text))}]";

    public string FormatFinalLiveTranscript(RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments)
        => $"[final-live:{group.DisplayName}:{string.Join(",", segments.Select(s => s.Text))}]";

    public string FormatBatchProgress(DateTime startedAt, IList<string> progressRows)
        => "[batch-progress]";

    public string FormatGroupTranscript(RecordingGroup group, IEnumerable<TranscriptSegment> segments)
        => $"[group-transcript:{group.BaseName}:{string.Join(",", segments.Select(s => $"{s.SpeakerLabel}:{s.Text}"))}]";

    public string FormatDiarizedTranscript(RecordingGroup group, IReadOnlyList<TranscriptSegment> segments, TranscriptDocument doc)
        => $"[diarized:{group.BaseName}:{string.Join(",", segments.Select(s => $"{s.SpeakerLabel}:{s.Text}"))}]";

    public string FormatSingleFileTranscript(TranscriptDocument doc, string audioPath)
        => $"[single-file:{Path.GetFileName(audioPath)}:{string.Join(",", doc.Segments.Select(s => s.Text))}]";
}

internal sealed class FakeSpeakerMergeStrategy : ISpeakerMergeStrategy
{
    public IReadOnlyList<TranscriptSegment> Merge(
        IReadOnlyList<TranscriptSegment> source,
        IReadOnlyList<SpeakerSegment> speakers)
        => source;
}

internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> ExistingFiles { get; } = [];
    public HashSet<string> ExistingDirectories { get; } = [];
    public List<string> DeletedFiles { get; } = [];

    public bool FileExists(string path) => ExistingFiles.Contains(path);
    public bool DirectoryExists(string path) => ExistingDirectories.Contains(path);
    public IEnumerable<string> GetFiles(string directoryPath) => [];
    public void DeleteFile(string path) => DeletedFiles.Add(path);
}
