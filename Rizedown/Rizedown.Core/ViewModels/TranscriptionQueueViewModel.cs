using Rizedown.AudioCapture;
using Rizedown.Features.Workspace;
using Rizedown.Models;
using Rizedown.Transcription;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;

namespace Rizedown.ViewModels;

public sealed record TranscriptionConfig(
    TranscriptionEngineType Engine,
    DiarizationEngineType Diarization,
    string WhisperBinaryPath,
    string WhisperModelPath,
    string PyannotePythonPath,
    string PyannoteHfToken,
    string SherpaSegmentationModelPath,
    string SherpaEmbeddingModelPath);

/// <summary>
/// Coordinates three concurrent transcription workstreams:
/// <list type="number">
///   <item><description>
///     <b>Live transcription</b> — during an active recording, accepts audio chunks via
///     <see cref="FeedLiveChunk"/> and feeds them to an <see cref="ILiveTranscriptionSession"/>,
///     publishing incremental progress through <see cref="EditorProgressUpdated"/>.
///   </description></item>
///   <item><description>
///     <b>Batch queue</b> — for recordings that don't support live transcription (or when
///     the live session is absent), groups are queued via <see cref="Enqueue"/> and processed
///     sequentially by <see cref="ITranscriptionPipeline.RunAsync"/>.
///   </description></item>
///   <item><description>
///     <b>Diarization post-processing</b> — after live transcription finishes, speaker
///     identification runs as a separate background pass and rewrites the transcript in place.
///   </description></item>
/// </list>
/// All three workstreams share a single <see cref="CancellationTokenSource"/>; call-site
/// state is coordinated through <see cref="MainViewModel"/>.
/// </summary>
public sealed class TranscriptionQueueViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when a new in-memory document should be loaded into the editor (e.g. progress view).</summary>
    public event EventHandler<MarkdownDocument>? LoadDocumentRequested;

    /// <summary>Raised when a saved transcript file should be opened in the editor by path.</summary>
    public event EventHandler<string>? OpenDocumentRequested;

    /// <summary>
    /// Raised when live transcription produces new content or batch progress advances.
    /// Fired at most once per 250 ms to avoid excessive layout updates.
    /// </summary>
    public event EventHandler<TranscriptionProgressEventArgs>? EditorProgressUpdated;

    /// <summary>Raised when transcript files are written or rotated and the workspace tree needs to reload.</summary>
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
    private DateTimeOffset _lastLiveUpdateAt;
    private readonly object _liveLock = new();

    // ── Diarization post-processing queue ─────────────────────────────────────
    private readonly List<DiarizationJob> _diarizationJobs = [];
    private bool _isDiarizing;

    // ── In-progress content cache (keyed by BaseName) ─────────────────────────
    private readonly Dictionary<string, string> _currentProgressContent = new(StringComparer.OrdinalIgnoreCase);

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
        DiarizeGroupCommand = new RelayCommand(async () => await DiarizeGroupOnlyAsync());
        TranscribeAudioCommand = new RelayCommand<WorkspaceTreeItem>(async item => await TranscribeAudioAsync(item));

        AppDomain.CurrentDomain.ProcessExit += (_, _) => _cts.Cancel();
        _workspace.PropertyChanged += OnWorkspaceStateChanged;
    }

    private void OnWorkspaceStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceExplorerState.WorkspaceRootPath))
            _ = ResumeQueueAsync();
    }

    public ICommand ReTranscribeGroupCommand { get; }
    public ICommand DiarizeGroupCommand { get; }
    public ICommand TranscribeAudioCommand { get; }

    public bool CanReTranscribeGroup =>
        _getSelectedGroup() is { HasTranscript: true } group &&
        !_jobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase));

    public bool CanDiarizeGroup
    {
        get
        {
            var group = _getSelectedGroup();
            return group is { HasTranscript: true } &&
                   (group.MicFilePath is not null || group.SysFilePath is not null) &&
                   _getConfig().Diarization != DiarizationEngineType.None &&
                   !_diarizationJobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase)) &&
                   !_jobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void NotifyCanReTranscribeGroupChanged()
    {
        OnPropertyChanged(nameof(CanReTranscribeGroup));
        OnPropertyChanged(nameof(CanDiarizeGroup));
    }

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
            // FlushAsync waits for all in-flight chunk recognitions before returning,
            // so the snapshot below captures every segment.
            await session.FlushAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FinalizeRecordingAsync: FlushAsync failed for {Name}", group.BaseName);
        }

        // Snapshot before unsubscribing so any segment that fires synchronously
        // during FlushAsync continuation is still captured.
        List<TranscriptSegment> segments;
        lock (_liveLock) { segments = [.. _liveSegments]; }

        session.SegmentsReady -= OnLiveSegmentsReady;
        await session.DisposeAsync();

        var transcriptPath = _storage.GetTranscriptPath(group);
        try
        {
            var markdown = _formatter.FormatFinalLiveTranscript(group, _liveStartedAt, segments);
            await _storage.WriteAsync(transcriptPath, markdown);
            _logger.LogInformation("Live transcript saved: {Path}", transcriptPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FinalizeRecordingAsync: failed to write transcript to {Path}", transcriptPath);
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
        bool throttled;

        lock (_liveLock)
        {
            _liveSegments.AddRange(segments);
            group = _liveGroup;
            startedAt = _liveStartedAt;
            var now = DateTimeOffset.UtcNow;
            throttled = (now - _lastLiveUpdateAt).TotalMilliseconds < 250;
            if (!throttled) _lastLiveUpdateAt = now;
        }

        if (group is null || throttled) return;

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
            while (true)
            {
                var job = _diarizationJobs.FirstOrDefault(j => j.Status == DiarizationJobStatus.Queued);
                if (job is null) break;

                job.Status = DiarizationJobStatus.Active;
                ApplyHighlights(_workspace.WorkspaceItems);
                try
                {
                    if (job.IsStandalone)
                        await DiarizeGroupStandaloneAsync(job.Group);
                    else
                        await DiarizeGroupAsync(job);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Diarization job failed for {Name}", job.Group.BaseName);
                }
                finally
                {
                    _diarizationJobs.Remove(job);
                    OnPropertyChanged(nameof(CanDiarizeGroup));
                    ApplyHighlights(_workspace.WorkspaceItems);
                    _ = SaveQueueAsync();
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
                config.PyannotePythonPath, config.PyannoteHfToken,
                config.SherpaSegmentationModelPath, config.SherpaEmbeddingModelPath);

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

            var fallbackCount = mergedSegments
                .Zip(job.LiveSegments, (merged, src) =>
                    string.Equals(merged.SpeakerLabel, src.SpeakerLabel, StringComparison.Ordinal))
                .Count(isFallback => isFallback);
            var uniqueSpeakers = mergedSegments
                .Select(s => s.SpeakerLabel)
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct(StringComparer.Ordinal)
                .Count();
            _logger.LogInformation(
                "Diarization merge: {Total} segments, {Speakers} unique speakers, {Fallbacks} unmatched (no overlap).",
                mergedSegments.Count, uniqueSpeakers, fallbackCount);

            // Rewrite the transcript with speaker-labelled segments.
            var transcriptPath = group.TranscriptPath ?? _storage.GetTranscriptPath(group);
            var content = _formatter.FormatDiarizedTranscript(group, mergedSegments, doc);
            await _storage.WriteAsync(transcriptPath, content);
            _logger.LogInformation("Diarization complete — transcript updated: {Path}", transcriptPath);

            WorkspaceRefreshNeeded?.Invoke(this, EventArgs.Empty);

            if (string.Equals(_getSelectedGroup()?.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase))
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
        OnPropertyChanged(nameof(CanDiarizeGroup));
        ApplyHighlights(_workspace.WorkspaceItems);
        _ = SaveQueueAsync();

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
            var wasTranscribing = item.IsActivelyTranscribing;
            item.IsActivelyTranscribing = string.Equals(baseName, activeTranscribeBaseName, StringComparison.OrdinalIgnoreCase);
            if (wasTranscribing && !item.IsActivelyTranscribing)
                item.TranscriptionProgress = 0;
            item.IsScheduledTranscription = _jobs.Any(j => j.Status == TranscriptionJobStatus.Queued &&
                string.Equals(j.Group.BaseName, baseName, StringComparison.OrdinalIgnoreCase));
            item.IsActivelyDiarizing = string.Equals(baseName, activeDiarizeBaseName, StringComparison.OrdinalIgnoreCase);
            item.IsScheduledDiarization = _diarizationJobs.Any(j => j.Status == DiarizationJobStatus.Queued &&
                string.Equals(j.Group.BaseName, baseName, StringComparison.OrdinalIgnoreCase));
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
                    OnPropertyChanged(nameof(CanDiarizeGroup));
                    ApplyHighlights(_workspace.WorkspaceItems);
                    _ = SaveQueueAsync();
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
        _currentProgressContent[group.BaseName] = _formatter.FormatBatchProgress(startedAt, progressRows);

        void PushProgressRow(string row, double pct)
        {
            if (progressRows.Count >= MaxProgressRows)
                progressRows[^1] = row;
            else
                progressRows.Add(row);
            foreach (var item in _workspace.WorkspaceItems)
                if (item.IsRecordingGroup && string.Equals(item.RecordingGroup!.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase))
                    item.TranscriptionProgress = pct;
            var content = _formatter.FormatBatchProgress(startedAt, progressRows);
            _currentProgressContent[group.BaseName] = content;
            EditorProgressUpdated?.Invoke(this, new TranscriptionProgressEventArgs
            {
                Group = group,
                Content = content
            });
        }

        var progress = new Progress<double>(pct =>
        {
            if (pct - lastReportedPercent < 0.05) return;
            lastReportedPercent = pct;
            PushProgressRow($"* {DateTime.Now:yyyy-MM-dd HH:mm:ss} ... {pct:P0}", pct);
        });

        try
        {
            var config = _getConfig();
            var pipeline = _pipelineFactory.Create(
                config.Engine, config.Diarization,
                config.WhisperBinaryPath, config.WhisperModelPath,
                config.PyannotePythonPath, config.PyannoteHfToken,
                config.SherpaSegmentationModelPath, config.SherpaEmbeddingModelPath);

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

            if (string.Equals(_getSelectedGroup()?.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase))
                OpenDocumentRequested?.Invoke(this, transcriptPath);

            _logger.LogInformation("Group transcription complete: {Path}", transcriptPath);
        }
        catch (Exception ex)
        {
            await _reportError("Transcription failed.", ex, ex.Message);
        }
        finally
        {
            _currentProgressContent.Remove(group.BaseName);
        }
    }

    private async Task ReTranscribeGroupAsync()
    {
        if (_getSelectedGroup() is not { } group) return;
        if (!group.HasTranscript || group.TranscriptPath is not { } transcriptPath) return;

        var rotated = _storage.GetRotatedPath(transcriptPath);
        await _storage.MoveAsync(transcriptPath, rotated);
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

    private Task DiarizeGroupOnlyAsync()
    {
        if (_getSelectedGroup() is not { } group) return Task.CompletedTask;
        if (group.MicFilePath is null && group.SysFilePath is null) return Task.CompletedTask;

        _logger.LogInformation("Diarize-only queued: {Name}", group.DisplayName);
        _diarizationJobs.Add(new DiarizationJob(group));
        OnPropertyChanged(nameof(CanDiarizeGroup));
        ApplyHighlights(_workspace.WorkspaceItems);
        _ = SaveQueueAsync();

        if (!_isDiarizing)
            _ = ProcessDiarizationQueueAsync();

        return Task.CompletedTask;
    }

    private async Task DiarizeGroupStandaloneAsync(RecordingGroup group)
    {
        _logger.LogInformation("Diarize-only: starting {Name}", group.DisplayName);

        var transcriptPath = group.TranscriptPath ?? _storage.GetTranscriptPath(group);
        var audioPath = group.MicFilePath ?? group.SysFilePath;
        if (audioPath is null) return;

        var content = await _storage.ReadAsync(transcriptPath);
        RecordingPathBuilder.TryParseRecordingStart(group.BaseName, out var recordingStart);
        var existingSegments = _formatter.ParseSegments(
            content, recordingStart == default ? null : recordingStart);

        if (existingSegments.Count == 0)
        {
            _logger.LogWarning("Diarize-only: no segments parsed from {Path}", transcriptPath);
            return;
        }

        var config = _getConfig();
        var engine = _pipelineFactory.CreateDiarizationEngine(
            config.Diarization,
            config.PyannotePythonPath, config.PyannoteHfToken,
            config.SherpaSegmentationModelPath, config.SherpaEmbeddingModelPath);

        var speakerSegments = await engine.DiarizeAsync(audioPath, progress: null, _cts.Token);
        if (speakerSegments.Count == 0)
        {
            _logger.LogInformation("Diarize-only: engine returned no speaker segments for {Name}", group.BaseName);
            return;
        }

        var merged = _mergeStrategy.Merge(existingSegments, speakerSegments);
        var doc = new TranscriptDocument
        {
            TranscriptionEngineName = "— (existing)",
            DiarizationEngineName   = engine.Name,
        };
        var updated = _formatter.FormatDiarizedTranscript(group, merged, doc);
        await _storage.WriteAsync(transcriptPath, updated);
        _logger.LogInformation("Diarize-only complete — {Speakers} speaker segments applied to {Name}.",
            speakerSegments.Count, group.DisplayName);

        WorkspaceRefreshNeeded?.Invoke(this, EventArgs.Empty);

        if (string.Equals(_getSelectedGroup()?.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase))
            OpenDocumentRequested?.Invoke(this, transcriptPath);
    }

    // ── Explorer in-progress display helpers ─────────────────────────────────

    /// <summary>Shows the current transcription progress doc for a group that is actively transcribing.</summary>
    public void ShowProgressForGroup(RecordingGroup group)
    {
        var content = _currentProgressContent.TryGetValue(group.BaseName, out var cached)
            ? cached
            : _formatter.FormatBatchProgress(DateTime.Now, []);
        LoadDocumentRequested?.Invoke(this, new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = content,
            IsUntitled = true,
            FileName = group.DisplayName
        });
    }

    /// <summary>Shows the existing transcript with a diarization-in-progress banner prepended.</summary>
    public async Task ShowDiarizationProgressForGroupAsync(RecordingGroup group)
    {
        string content;
        var transcriptPath = group.TranscriptPath ?? _storage.GetTranscriptPath(group);
        if (File.Exists(transcriptPath))
        {
            var transcript = await _storage.ReadAsync(transcriptPath);
            content = $"> **Diarization in progress…**\n\n---\n\n{transcript}";
        }
        else
        {
            content = $"# {group.DisplayName}\n\n> **Diarization in progress…**";
        }
        LoadDocumentRequested?.Invoke(this, new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = content,
            IsUntitled = true,
            FileName = group.DisplayName
        });
    }

    /// <summary>Shows a placeholder doc for a group scheduled but not yet started.</summary>
    public void ShowScheduledPlaceholder(RecordingGroup group, bool isDiarization)
    {
        var verb = isDiarization ? "Diarization" : "Transcription";
        LoadDocumentRequested?.Invoke(this, new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = $"# {group.DisplayName}\n\n> **{verb} scheduled…**\n\nThis job is queued and will begin when the current job completes.",
            IsUntitled = true,
            FileName = group.DisplayName
        });
    }

    // ── Queue persistence ─────────────────────────────────────────────────────

    private async Task SaveQueueAsync()
    {
        if (string.IsNullOrWhiteSpace(_workspace.WorkspaceRootPath)) return;
        var path = Path.Combine(_workspace.WorkspaceRootPath, ".rizedown-queue.json");
        try
        {
            var dto = new PersistedQueueDto
            {
                TranscriptionJobs = _jobs
                    .Select(j => new PersistedJobDto
                    {
                        BaseName = j.Group.BaseName,
                        DirectoryPath = j.Group.DirectoryPath,
                        MicFilePath = j.Group.MicFilePath,
                        SysFilePath = j.Group.SysFilePath
                    }).ToList(),
                DiarizationJobs = _diarizationJobs
                    .Where(j => j.IsStandalone)
                    .Select(j => new PersistedJobDto
                    {
                        BaseName = j.Group.BaseName,
                        DirectoryPath = j.Group.DirectoryPath,
                        MicFilePath = j.Group.MicFilePath,
                        SysFilePath = j.Group.SysFilePath
                    }).ToList()
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(dto));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save job queue to {Path}", path);
        }
    }

    private async Task ResumeQueueAsync()
    {
        if (string.IsNullOrWhiteSpace(_workspace.WorkspaceRootPath)) return;
        var path = Path.Combine(_workspace.WorkspaceRootPath, ".rizedown-queue.json");
        if (!File.Exists(path)) return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var dto = JsonSerializer.Deserialize<PersistedQueueDto>(json);
            if (dto is null) return;

            var restored = 0;
            foreach (var job in dto.TranscriptionJobs)
            {
                var group = new RecordingGroup
                {
                    BaseName = job.BaseName, DirectoryPath = job.DirectoryPath,
                    MicFilePath = job.MicFilePath, SysFilePath = job.SysFilePath
                };
                if (File.Exists(_storage.GetTranscriptPath(group))) continue;
                if (_jobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase))) continue;
                _jobs.Add(new TranscriptionJob(group));
                restored++;
            }

            foreach (var job in dto.DiarizationJobs)
            {
                var group = new RecordingGroup
                {
                    BaseName = job.BaseName, DirectoryPath = job.DirectoryPath,
                    MicFilePath = job.MicFilePath, SysFilePath = job.SysFilePath
                };
                if (_diarizationJobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase))) continue;
                _diarizationJobs.Add(new DiarizationJob(group));
                restored++;
            }

            if (restored > 0)
            {
                _logger.LogInformation("Restored {N} job(s) from queue file.", restored);
                OnPropertyChanged(nameof(CanReTranscribeGroup));
                OnPropertyChanged(nameof(CanDiarizeGroup));
                ApplyHighlights(_workspace.WorkspaceItems);
                if (!_isProcessing && _jobs.Count > 0) _ = ProcessQueueAsync();
                if (!_isDiarizing && _diarizationJobs.Any(j => j.Status == DiarizationJobStatus.Queued)) _ = ProcessDiarizationQueueAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore job queue from {Path}", path);
        }
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
                config.PyannotePythonPath, config.PyannoteHfToken,
                config.SherpaSegmentationModelPath, config.SherpaEmbeddingModelPath);

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
    public bool IsStandalone { get; }

    public DiarizationJob(RecordingGroup group, IReadOnlyList<TranscriptSegment> liveSegments)
    {
        Group = group;
        LiveSegments = liveSegments;
        Status = DiarizationJobStatus.Queued;
        IsStandalone = false;
    }

    // Standalone diarization (Diarize button — no live segments, reads from existing transcript)
    public DiarizationJob(RecordingGroup group)
    {
        Group = group;
        LiveSegments = [];
        Status = DiarizationJobStatus.Queued;
        IsStandalone = true;
    }
}

internal sealed class PersistedQueueDto
{
    public List<PersistedJobDto> TranscriptionJobs { get; set; } = [];
    public List<PersistedJobDto> DiarizationJobs { get; set; } = [];
}

internal sealed class PersistedJobDto
{
    public string BaseName { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string? MicFilePath { get; set; }
    public string? SysFilePath { get; set; }
}
