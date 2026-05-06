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

internal sealed class FakeSynchronousDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) => action();
    public Task InvokeOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
    public Task InvokeOnMainThreadAsync(Func<Task> action) => action();
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
    public AudioCaptureResult ResultToReturn { get; set; } = new() { Success = true };

    public event EventHandler<AudioCaptureState>? StateChanged;

    public Task<AudioPermissionStatus> CheckMicrophonePermissionAsync() => Task.FromResult(PermissionToReturn);
    public Task<AudioPermissionStatus> RequestMicrophonePermissionAsync() => Task.FromResult(PermissionToReturn);

    public Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
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

    public event EventHandler? PlaybackStateChanged;

    public Task PlayAsync(string filePath)
    {
        CurrentlyPlayingPath = filePath;
        IsPlaying = true;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Pause() { IsPlaying = false; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); }
    public void Stop() { IsPlaying = false; CurrentlyPlayingPath = null; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); }
}

internal sealed class FakeTranscriptionPipelineFactory : ITranscriptionPipelineFactory
{
    public IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines { get; } = [];
    public IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines { get; } = [];

    public ITranscriptionPipeline Create(
        TranscriptionEngineType engine, DiarizationEngineType diarization,
        string whisperBinaryPath = "", string whisperModelPath = "",
        string pyannotePythonPath = "", string pyannoteHfToken = "")
        => throw new NotSupportedException("Transcription not expected in unit tests.");
}
