using MauiMds.AudioCapture;
using MauiMds.Features.Workspace;
using MauiMds.Models;
using MauiMds.Transcription;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MauiMds.ViewModels;

public sealed record TranscriptionConfig(
    TranscriptionEngineType Engine,
    DiarizationEngineType Diarization,
    string WhisperBinaryPath,
    string WhisperModelPath,
    string PyannotePythonPath,
    string PyannoteHfToken);

public sealed class TranscriptionQueueViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MarkdownDocument>? LoadDocumentRequested;
    public event EventHandler<string>? OpenDocumentRequested;
    public event EventHandler<TranscriptionProgressEventArgs>? EditorProgressUpdated;
    public event EventHandler? WorkspaceRefreshNeeded;

    private readonly ITranscriptionPipelineFactory _pipelineFactory;
    private readonly ITranscriptStorage _storage;
    private readonly ITranscriptFormatter _formatter;
    private readonly ISpeakerMergeStrategy _mergeStrategy;
    private readonly WorkspaceExplorerState _workspace;
    private readonly ILogger<TranscriptionQueueViewModel> _logger;
    private readonly IMainThreadDispatcher _mainThreadDispatcher;
    private readonly IApplicationLifetime _applicationLifetime;
    private readonly IAlertService _alertService;
    private readonly Func<TranscriptionConfig> _getConfig;
    private readonly Func<RecordingGroup?> _getSelectedGroup;
    private readonly Action<RecordingGroup?> _setSelectedGroup;
    private readonly Func<string, Exception?, string, Task> _reportError;
    private readonly Action<string> _setStatus;

    private const int MaxProgressRows = 20;

    // ── Transcription queue (batch / re-transcribe) ───────────────────────────
    private readonly List<TranscriptionJob> _jobs = [];
    private readonly CancellationTokenSource _cts = new();
    private bool _isProcessing;

    // ── Live transcription state ──────────────────────────────────────────────
    private ILiveTranscriptionSession? _activeLiveSession;
    private RecordingGroup? _liveGroup;
    private readonly List<TranscriptSegment> _liveSegments = [];
    private DateTime _liveStartedAt;
    private readonly object _liveLock = new();

    // ── Diarization post-processing queue ─────────────────────────────────────
    private readonly List<DiarizationJob> _diarizationJobs = [];
    private bool _isDiarizing;

    public TranscriptionQueueViewModel(
        ITranscriptionPipelineFactory pipelineFactory,
        ITranscriptStorage storage,
        ITranscriptFormatter formatter,
        ISpeakerMergeStrategy mergeStrategy,
        WorkspaceExplorerState workspace,
        ILogger<TranscriptionQueueViewModel> logger,
        IMainThreadDispatcher mainThreadDispatcher,
        IApplicationLifetime applicationLifetime,
        IAlertService alertService,
        Func<TranscriptionConfig> getConfig,
        Func<RecordingGroup?> getSelectedGroup,
        Action<RecordingGroup?> setSelectedGroup,
        Func<string, Exception?, string, Task> reportError,
        Action<string> setStatus)
    {
        _pipelineFactory = pipelineFactory;
        _storage = storage;
        _formatter = formatter;
        _mergeStrategy = mergeStrategy;
        _workspace = workspace;
        _logger = logger;
        _mainThreadDispatcher = mainThreadDispatcher;
        _applicationLifetime = applicationLifetime;
        _alertService = alertService;
        _getConfig = getConfig;
        _getSelectedGroup = getSelectedGroup;
        _setSelectedGroup = setSelectedGroup;
        _reportError = reportError;
        _setStatus = setStatus;

        ReTranscribeGroupCommand = new RelayCommand(async () => await ReTranscribeGroupAsync());
        TranscribeAudioCommand = new RelayCommand<WorkspaceTreeItem>(async item => await TranscribeAudioAsync(item));

        AppDomain.CurrentDomain.ProcessExit += (_, _) => _cts.Cancel();
    }

    public ICommand ReTranscribeGroupCommand { get; }
    public ICommand TranscribeAudioCommand { get; }

    public bool CanReTranscribeGroup =>
        _getSelectedGroup() is { HasTranscript: true } group &&
        !_jobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase));

    public void NotifyCanReTranscribeGroupChanged()
        => OnPropertyChanged(nameof(CanReTranscribeGroup));

    // ── Live transcription ────────────────────────────────────────────────────

    /// <summary>
    /// Called when recording starts. Creates a live transcription session and shows an
    /// in-progress document in the editor. Does nothing if the configured engine does not
    /// support live transcription.
    /// </summary>
    public void StartLiveTranscription(RecordingGroup group, INativeMicrophoneSource? nativeMicSource)
    {
        lock (_liveLock)
        {
            if (_activeLiveSession is not null)
            {
                _logger.LogWarning("StartLiveTranscription called while a session is already active — ignoring.");
                return;
            }

            var config = _getConfig();
            var session = _pipelineFactory.CreateLiveSession(
                config.Engine,
                config.WhisperBinaryPath,
                config.WhisperModelPath,
                nativeMicSource);

            if (session is null)
            {
                _logger.LogInformation("Engine {Engine} does not support live transcription — will batch-transcribe after stop.",
                    config.Engine);
                return;
            }

            _activeLiveSession = session;
            _liveGroup = group;
            _liveSegments.Clear();
            _liveStartedAt = DateTime.Now;

            session.SegmentsReady += OnLiveSegmentsReady;
        }

        // Show the live progress document in the editor immediately.
        var progressDoc = new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = _formatter.FormatLiveProgress(group, _liveStartedAt, _liveSegments),
            IsUntitled = true,
            FileName = group.DisplayName
        };
        LoadDocumentRequested?.Invoke(this, progressDoc);

        _logger.LogInformation("Live transcription started for group {Name}.", group.BaseName);
    }

    /// <summary>Feed an audio chunk from the capture service to the active live session.</summary>
    public void FeedLiveChunk(LiveAudioChunk chunk)
    {
        ILiveTranscriptionSession? session;
        lock (_liveLock) { session = _activeLiveSession; }
        if (session is null) return;

        _ = session.FeedChunkAsync(chunk.WavFilePath, chunk.StartOffset, _cts.Token);
    }

    /// <summary>
    /// Called when recording stops. Flushes the live session, writes the transcript to disk,
    /// selects the group, and queues diarization post-processing if configured.
    /// Falls back to batch transcription if no live session is active.
    /// </summary>
    public async Task FinalizeRecordingAsync(RecordingGroup group)
    {
        ILiveTranscriptionSession? session;
        lock (_liveLock)
        {
            session = _activeLiveSession;
            _activeLiveSession = null;
        }

        if (session is null)
        {
            // No live session — fall back to the existing batch path.
            EnqueueWithProgressDocument(group);
            return;
        }

        try
        {
            _logger.LogInformation("Finalizing live transcript for {Name}.", group.BaseName);
            await session.FlushAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FinalizeRecordingAsync: FlushAsync failed for {Name}", group.BaseName);
        }
        finally
        {
            await session.DisposeAsync();
        }

        // Snapshot the accumulated segments.
        List<TranscriptSegment> segments;
        lock (_liveLock) { segments = [.. _liveSegments]; }

        var transcriptPath = _storage.GetTranscriptPath(group);
        try
        {
            var markdown = _formatter.FormatFinalLiveTranscript(group, _liveStartedAt, segments);
            await _storage.WriteAsync(transcriptPath, markdown);
            _logger.LogInformation("Live transcript saved: {Path}", transcriptPath);
        }
        catch (Exception ex)
        {
            await _reportError("Failed to save transcript.", ex, ex.Message);
            return;
        }

        WorkspaceRefreshNeeded?.Invoke(this, EventArgs.Empty);

        var savedGroup = new RecordingGroup
        {
            BaseName = group.BaseName,
            DirectoryPath = group.DirectoryPath,
            MicFilePath = group.MicFilePath,
            SysFilePath = group.SysFilePath,
            TranscriptPath = transcriptPath
        };

        _setSelectedGroup(savedGroup);
        OpenDocumentRequested?.Invoke(this, transcriptPath);

        // If diarization is configured, post-process to assign speaker labels.
        var config = _getConfig();
        if (config.Diarization != DiarizationEngineType.None)
            EnqueueDiarization(savedGroup, [.. segments]);
    }

    private void OnLiveSegmentsReady(object? sender, IReadOnlyList<TranscriptSegment> segments)
    {
        RecordingGroup? group;
        DateTime startedAt;

        lock (_liveLock)
        {
            _liveSegments.AddRange(segments);
            group = _liveGroup;
            startedAt = _liveStartedAt;
        }

        if (group is null) return;

        var content = _formatter.FormatLiveProgress(group, startedAt, _liveSegments);
        EditorProgressUpdated?.Invoke(this, new TranscriptionProgressEventArgs
        {
            Group = group,
            Content = content
        });
    }

    // ── Diarization post-processing ───────────────────────────────────────────

    private void EnqueueDiarization(RecordingGroup group, IReadOnlyList<TranscriptSegment> liveSegments)
    {
        _diarizationJobs.Add(new DiarizationJob(group, liveSegments));
        ApplyHighlights(_workspace.WorkspaceItems);
        _logger.LogInformation("Diarization queued for {Name}.", group.BaseName);

        if (!_isDiarizing)
            _ = ProcessDiarizationQueueAsync();
    }

    private async Task ProcessDiarizationQueueAsync()
    {
        if (_isDiarizing) return;
        _isDiarizing = true;
        try
        {
            while (_diarizationJobs.Count > 0)
            {
                var job = _diarizationJobs[0];
                job.Status = DiarizationJobStatus.Active;
                ApplyHighlights(_workspace.WorkspaceItems);
                try
                {
                    await DiarizeGroupAsync(job);
                }
                finally
                {
                    _diarizationJobs.Remove(job);
                    ApplyHighlights(_workspace.WorkspaceItems);
                }
            }
        }
        finally
        {
            _isDiarizing = false;
        }
    }

    private async Task DiarizeGroupAsync(DiarizationJob job)
    {
        var group = job.Group;
        _logger.LogInformation("Diarization starting for {Name}.", group.BaseName);

        try
        {
            var config = _getConfig();
            var pipeline = _pipelineFactory.Create(
                config.Engine, config.Diarization,
                config.WhisperBinaryPath, config.WhisperModelPath,
                config.PyannotePythonPath, config.PyannoteHfToken);

            // Run diarization on the mic file (primary speaker identification source).
            var audioPath = group.MicFilePath ?? group.SysFilePath;
            if (audioPath is null) return;

            var doc = await pipeline.RunAsync(audioPath, progress: null, _cts.Token);
            if (doc.Segments.Count == 0 || doc.DiarizationEngineName == "None")
            {
                _logger.LogInformation("Diarization produced no speaker labels for {Name}.", group.BaseName);
                return;
            }

            // Merge diarization speaker labels into the live segments by timestamp overlap.
            var speakerSegments = doc.Segments
                .Select(s => new SpeakerSegment { SpeakerLabel = s.SpeakerLabel ?? "Speaker", Start = s.Start, End = s.End })
                .ToList();
            var mergedSegments = _mergeStrategy.Merge(job.LiveSegments, speakerSegments);

            // Rewrite the transcript with speaker-labelled segments.
            var transcriptPath = group.TranscriptPath ?? _storage.GetTranscriptPath(group);
            var content = _formatter.FormatDiarizedTranscript(group, mergedSegments, doc);
            await _storage.WriteAsync(transcriptPath, content);
            _logger.LogInformation("Diarization complete — transcript updated: {Path}", transcriptPath);

            WorkspaceRefreshNeeded?.Invoke(this, EventArgs.Empty);

            if (ReferenceEquals(_getSelectedGroup()?.BaseName, group.BaseName))
                OpenDocumentRequested?.Invoke(this, transcriptPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Diarization failed for {Name}", group.BaseName);
        }
    }

    // ── Batch transcription queue (re-transcribe / fallback) ──────────────────

    public void EnqueueWithProgressDocument(RecordingGroup group)
    {
        var progressDoc = new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = _formatter.FormatBatchProgress(DateTime.Now, []),
            IsUntitled = true,
            FileName = group.DisplayName
        };
        LoadDocumentRequested?.Invoke(this, progressDoc);
        Enqueue(group);
    }

    public void Enqueue(RecordingGroup group)
    {
        if (_jobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Group {Name} is already queued — skipping duplicate.", group.BaseName);
            return;
        }

        _jobs.Add(new TranscriptionJob(group));
        _logger.LogInformation("Group {Name} added to transcription queue (depth: {Depth}).",
            group.BaseName, _jobs.Count);
        OnPropertyChanged(nameof(CanReTranscribeGroup));
        ApplyHighlights(_workspace.WorkspaceItems);

        if (!_isProcessing)
            _ = ProcessQueueAsync();
    }

    public void ApplyHighlights(IEnumerable<WorkspaceTreeItem> items)
    {
        var activeTranscribeBaseName = _jobs
            .FirstOrDefault(j => j.Status == TranscriptionJobStatus.Active)?.Group.BaseName;
        var activeDiarizeBaseName = _diarizationJobs
            .FirstOrDefault(j => j.Status == DiarizationJobStatus.Active)?.Group.BaseName;

        foreach (var item in items)
        {
            if (!item.IsRecordingGroup) continue;
            var baseName = item.RecordingGroup!.BaseName;
            item.IsActivelyTranscribing = string.Equals(baseName, activeTranscribeBaseName, StringComparison.OrdinalIgnoreCase);
            item.IsInTranscriptionQueue = _jobs.Any(j => j.Status == TranscriptionJobStatus.Queued &&
                string.Equals(j.Group.BaseName, baseName, StringComparison.OrdinalIgnoreCase));
            item.IsActivelyDiarizing = string.Equals(baseName, activeDiarizeBaseName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task ProcessQueueAsync()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try
        {
            while (true)
            {
                var job = _jobs.FirstOrDefault(j => j.Status == TranscriptionJobStatus.Queued);
                if (job is null) break;

                job.Status = TranscriptionJobStatus.Active;
                ApplyHighlights(_workspace.WorkspaceItems);

                try
                {
                    await TranscribeGroupAsync(job.Group);
                }
                finally
                {
                    _jobs.Remove(job);
                    OnPropertyChanged(nameof(CanReTranscribeGroup));
                    ApplyHighlights(_workspace.WorkspaceItems);
                    _logger.LogInformation("Group {Name} removed from queue (remaining: {N}).",
                        job.Group.BaseName, _jobs.Count);
                }
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async Task TranscribeGroupAsync(RecordingGroup group)
    {
        if (group.AudioFilePaths.Count == 0) return;

        var startedAt = DateTime.Now;
        var progressRows = new List<string>();
        double lastReportedPercent = -0.06;

        void PushProgressRow(string row)
        {
            progressRows.Add(row);
            var content = _formatter.FormatBatchProgress(startedAt, progressRows);
            EditorProgressUpdated?.Invoke(this, new TranscriptionProgressEventArgs
            {
                Group = group,
                Content = content
            });
        }

        var progress = new Progress<double>(pct =>
        {
            if (progressRows.Count >= MaxProgressRows) return;
            if (pct - lastReportedPercent < 0.05) return;
            lastReportedPercent = pct;
            PushProgressRow($"* {DateTime.Now:yyyy-MM-dd HH:mm:ss} ... {pct:P0}");
        });

        try
        {
            var config = _getConfig();
            var pipeline = _pipelineFactory.Create(
                config.Engine, config.Diarization,
                config.WhisperBinaryPath, config.WhisperModelPath,
                config.PyannotePythonPath, config.PyannoteHfToken);

            var allSegments = new List<TranscriptSegment>();

            if (group.MicFilePath is { } micPath)
            {
                var micDoc = await pipeline.RunAsync(micPath, progress, _cts.Token);
                foreach (var seg in micDoc.Segments)
                {
                    allSegments.Add(new TranscriptSegment
                    {
                        Start = seg.Start, End = seg.End, Text = seg.Text, Confidence = seg.Confidence,
                        SpeakerLabel = !string.IsNullOrEmpty(seg.SpeakerLabel) ? seg.SpeakerLabel : "Microphone"
                    });
                }
            }

            if (group.SysFilePath is { } sysPath)
            {
                var sysDoc = await pipeline.RunAsync(sysPath, progress, _cts.Token);
                foreach (var seg in sysDoc.Segments)
                {
                    allSegments.Add(new TranscriptSegment
                    {
                        Start = seg.Start, End = seg.End, Text = seg.Text, Confidence = seg.Confidence,
                        SpeakerLabel = !string.IsNullOrEmpty(seg.SpeakerLabel) ? seg.SpeakerLabel : "System Audio"
                    });
                }
            }

            var transcriptPath = _storage.GetTranscriptPath(group);
            var transcriptContent = _formatter.FormatGroupTranscript(group, allSegments.OrderBy(s => s.Start));
            await _storage.WriteAsync(transcriptPath, transcriptContent);

            WorkspaceRefreshNeeded?.Invoke(this, EventArgs.Empty);

            if (ReferenceEquals(_getSelectedGroup(), group))
                OpenDocumentRequested?.Invoke(this, transcriptPath);

            _logger.LogInformation("Group transcription complete: {Path}", transcriptPath);
        }
        catch (Exception ex)
        {
            await _reportError("Transcription failed.", ex, ex.Message);
        }
    }

    private async Task ReTranscribeGroupAsync()
    {
        if (_getSelectedGroup() is not { } group) return;
        if (!group.HasTranscript || group.TranscriptPath is not { } transcriptPath) return;

        var rotated = _storage.GetRotatedPath(transcriptPath);
        _storage.Move(transcriptPath, rotated);
        _logger.LogInformation("Re-transcribe: {Name} — existing transcript rotated to {Rotated}.",
            group.DisplayName, rotated);

        WorkspaceRefreshNeeded?.Invoke(this, EventArgs.Empty);

        var freshGroup = new RecordingGroup
        {
            BaseName = group.BaseName,
            DirectoryPath = group.DirectoryPath,
            MicFilePath = group.MicFilePath,
            SysFilePath = group.SysFilePath,
            TranscriptPath = null
        };

        _setSelectedGroup(freshGroup);

        var progressDoc = new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = _formatter.FormatBatchProgress(DateTime.Now, []),
            IsUntitled = true,
            FileName = freshGroup.DisplayName
        };
        LoadDocumentRequested?.Invoke(this, progressDoc);
        Enqueue(freshGroup);
    }

    private async Task TranscribeAudioAsync(WorkspaceTreeItem? item)
    {
        if (item is null) return;

        var audioPath = item.FullPath;
        var transcriptPath = Path.Combine(
            Path.GetDirectoryName(audioPath)!,
            Path.GetFileNameWithoutExtension(audioPath) + "_transcript.md");

        _setStatus("Transcribing…");
        try
        {
            var config = _getConfig();
            var pipeline = _pipelineFactory.Create(
                config.Engine, config.Diarization,
                config.WhisperBinaryPath, config.WhisperModelPath,
                config.PyannotePythonPath, config.PyannoteHfToken);

            var progress = new Progress<double>(v =>
                _mainThreadDispatcher.BeginInvokeOnMainThread(() => _setStatus($"Transcribing… {v:P0}")));

            var doc = await pipeline.RunAsync(audioPath, progress);
            var markdown = _formatter.FormatSingleFileTranscript(doc, audioPath);
            await _storage.WriteAsync(transcriptPath, markdown);

            _setStatus(string.Empty);

            await _alertService.ShowAlertAsync("Transcription Complete",
                $"Transcript saved to:\n{Path.GetFileName(transcriptPath)}", "OK");
        }
        catch (Exception ex)
        {
            await _reportError("Transcription failed.", ex, ex.Message);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_applicationLifetime.IsTerminating) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TranscriptionProgressEventArgs : EventArgs
{
    public RecordingGroup Group { get; init; } = null!;
    public string Content { get; init; } = string.Empty;
}

internal enum TranscriptionJobStatus { Queued, Active }
internal enum DiarizationJobStatus { Queued, Active }

internal sealed class TranscriptionJob
{
    public RecordingGroup Group { get; }
    public TranscriptionJobStatus Status { get; set; }
    public TranscriptionJob(RecordingGroup group)
    {
        Group = group;
        Status = TranscriptionJobStatus.Queued;
    }
}

internal sealed class DiarizationJob
{
    public RecordingGroup Group { get; }
    public IReadOnlyList<TranscriptSegment> LiveSegments { get; }
    public DiarizationJobStatus Status { get; set; }
    public DiarizationJob(RecordingGroup group, IReadOnlyList<TranscriptSegment> liveSegments)
    {
        Group = group;
        LiveSegments = liveSegments;
        Status = DiarizationJobStatus.Queued;
    }
}
