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
            Content = BuildLiveTranscriptMarkdown(group, _liveStartedAt, _liveSegments),
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

        var transcriptPath = Path.Combine(group.DirectoryPath, group.BaseName + "_transcript.md");
        try
        {
            var markdown = BuildFinalLiveTranscriptMarkdown(group, _liveStartedAt, segments);
            await File.WriteAllTextAsync(transcriptPath, markdown);
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

        var content = BuildLiveTranscriptMarkdown(group, startedAt, _liveSegments);
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
            var mergedSegments = MergeDiarizationIntoLive(job.LiveSegments, doc.Segments);

            // Rewrite the transcript with speaker-labelled segments.
            var transcriptPath = group.TranscriptPath
                ?? Path.Combine(group.DirectoryPath, group.BaseName + "_transcript.md");

            RecordingPathBuilder.TryParseRecordingStart(group.BaseName, out var groupRecordingStart);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Transcript: {group.DisplayName}");
            sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Engine: {doc.TranscriptionEngineName} | {doc.DiarizationEngineName}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            AppendSpeakerGroupedSegments(sb,
                mergedSegments.Select(s => (s.Start, s.End, s.SpeakerLabel ?? "Speaker", s.Text)),
                groupRecordingStart == default ? null : groupRecordingStart);

            await File.WriteAllTextAsync(transcriptPath, sb.ToString());
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

    private static IReadOnlyList<TranscriptSegment> MergeDiarizationIntoLive(
        IReadOnlyList<TranscriptSegment> liveSegments,
        IReadOnlyList<TranscriptSegment> diarizedSegments)
    {
        // diarizedSegments come from StandardTranscriptionPipeline which already has
        // speaker labels assigned from pyannote by overlap. We match live segments to
        // diarized segments by finding the best timestamp overlap.
        var result = new List<TranscriptSegment>(liveSegments.Count);
        foreach (var live in liveSegments)
        {
            string? bestLabel = null;
            var bestOverlap = TimeSpan.Zero;
            foreach (var diar in diarizedSegments)
            {
                var overlapStart = live.Start > diar.Start ? live.Start : diar.Start;
                var overlapEnd   = live.End   < diar.End   ? live.End   : diar.End;
                var overlap = overlapEnd - overlapStart;
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestLabel = diar.SpeakerLabel;
                }
            }
            result.Add(new TranscriptSegment
            {
                Text         = live.Text,
                Start        = live.Start,
                End          = live.End,
                Confidence   = live.Confidence,
                SpeakerLabel = bestLabel ?? live.SpeakerLabel ?? "Speaker"
            });
        }
        return result;
    }

    // ── Batch transcription queue (re-transcribe / fallback) ──────────────────

    public void EnqueueWithProgressDocument(RecordingGroup group)
    {
        var progressDoc = new MarkdownDocument
        {
            FilePath = string.Empty,
            Content = BuildTranscriptionProgressMarkdown(DateTime.Now, []),
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
            var content = BuildTranscriptionProgressMarkdown(startedAt, progressRows);
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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Transcript: {group.DisplayName}");
            sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            RecordingPathBuilder.TryParseRecordingStart(group.BaseName, out var groupRecordingStart);

            var allSegments = new List<(TimeSpan Start, TimeSpan End, string Speaker, string Text)>();

            if (group.MicFilePath is { } micPath)
            {
                var micDoc = await pipeline.RunAsync(micPath, progress, _cts.Token);
                foreach (var seg in micDoc.Segments)
                {
                    // Use diarization label if present; otherwise neutral source label.
                    var label = !string.IsNullOrEmpty(seg.SpeakerLabel)
                        ? seg.SpeakerLabel
                        : "Microphone";
                    allSegments.Add((seg.Start, seg.End, label, seg.Text));
                }
            }

            if (group.SysFilePath is { } sysPath)
            {
                var sysDoc = await pipeline.RunAsync(sysPath, progress, _cts.Token);
                foreach (var seg in sysDoc.Segments)
                {
                    var label = !string.IsNullOrEmpty(seg.SpeakerLabel)
                        ? seg.SpeakerLabel
                        : "System Audio";
                    allSegments.Add((seg.Start, seg.End, label, seg.Text));
                }
            }

            AppendSpeakerGroupedSegments(sb, allSegments.OrderBy(s => s.Start),
                groupRecordingStart == default ? null : groupRecordingStart);

            var transcriptPath = Path.Combine(group.DirectoryPath, group.BaseName + "_transcript.md");
            await File.WriteAllTextAsync(transcriptPath, sb.ToString());

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

        var rotated = GetRotatedTranscriptPath(transcriptPath);
        File.Move(transcriptPath, rotated);
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
            Content = BuildTranscriptionProgressMarkdown(DateTime.Now, []),
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
            var markdown = BuildTranscriptMarkdown(doc, audioPath);
            await File.WriteAllTextAsync(transcriptPath, markdown);

            _setStatus(string.Empty);

            await _alertService.ShowAlertAsync("Transcription Complete",
                $"Transcript saved to:\n{Path.GetFileName(transcriptPath)}", "OK");
        }
        catch (Exception ex)
        {
            await _reportError("Transcription failed.", ex, ex.Message);
        }
    }

    // ── Markdown builders ─────────────────────────────────────────────────────

    private static string BuildLiveTranscriptMarkdown(
        RecordingGroup group,
        DateTime startedAt,
        IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Live Transcript: {group.DisplayName}");
        sb.AppendLine($"Recording started: {startedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        if (segments.Count == 0)
        {
            sb.AppendLine("*Transcription in progress…*");
            return sb.ToString();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        AppendSpeakerGroupedSegments(sb,
            segments.Select(s => (s.Start, s.End, s.SpeakerLabel ?? "Speaker", s.Text)),
            recordingStart: startedAt);
        return sb.ToString();
    }

    private static string BuildFinalLiveTranscriptMarkdown(
        RecordingGroup group,
        DateTime startedAt,
        IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Transcript: {group.DisplayName}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        if (segments.Count == 0)
        {
            sb.AppendLine("*No speech detected.*");
            return sb.ToString();
        }

        AppendSpeakerGroupedSegments(sb,
            segments.Select(s => (s.Start, s.End, s.SpeakerLabel ?? "Speaker", s.Text)),
            recordingStart: startedAt);
        return sb.ToString();
    }

    private static string BuildTranscriptionProgressMarkdown(DateTime startedAt, IList<string> progressRows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Audio File Found. Beginning Transcription.");
        sb.AppendLine();
        sb.AppendLine($"* {startedAt:yyyy-MM-dd HH:mm:ss} Transcription Started");
        foreach (var row in progressRows)
            sb.AppendLine(row);
        return sb.ToString();
    }

    private static string BuildTranscriptMarkdown(TranscriptDocument doc, string audioPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Transcript: {Path.GetFileName(audioPath)}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Engine: {doc.TranscriptionEngineName} | {doc.DiarizationEngineName}");
        sb.AppendLine($"Duration: {doc.Duration:hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var baseName = Path.GetFileNameWithoutExtension(audioPath);
        RecordingPathBuilder.TryParseGroupFile(baseName, out var fileBaseName, out _);
        DateTime? startTime = RecordingPathBuilder.TryParseRecordingStart(
            string.IsNullOrEmpty(fileBaseName) ? baseName : fileBaseName, out var t) ? t : null;

        AppendSpeakerGroupedSegments(sb,
            doc.Segments.Select(s => (s.Start, s.End,
                !string.IsNullOrEmpty(s.SpeakerLabel) ? s.SpeakerLabel : "Speaker",
                s.Text)),
            startTime);

        return sb.ToString();
    }

    private static void AppendSpeakerGroupedSegments(
        System.Text.StringBuilder sb,
        IEnumerable<(TimeSpan Start, TimeSpan End, string Speaker, string Text)> segments,
        DateTime? recordingStart = null)
    {
        if (recordingStart is null)
            sb.AppendLine("> *All timestamps relative to recording start time.*");

        string? currentSpeaker = null;

        foreach (var seg in segments)
        {
            if (!string.Equals(seg.Speaker, currentSpeaker, StringComparison.Ordinal))
            {
                if (currentSpeaker is not null) sb.AppendLine();
                sb.AppendLine($"### {seg.Speaker}");
                currentSpeaker = seg.Speaker;
            }

            string startTs, endTs;
            if (recordingStart is { } rs)
            {
                startTs = (rs + seg.Start).ToString("HH:mm:ss");
                endTs   = (rs + seg.End).ToString("HH:mm:ss");
            }
            else
            {
                startTs = seg.Start.ToString(@"hh\:mm\:ss");
                endTs   = seg.End.ToString(@"hh\:mm\:ss");
            }

            sb.AppendLine($"> *[{startTs} – {endTs}]* {seg.Text}");
        }

        if (currentSpeaker is not null) sb.AppendLine();
    }

    private static string GetRotatedTranscriptPath(string transcriptPath)
    {
        var dir  = Path.GetDirectoryName(transcriptPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(transcriptPath);
        var ext  = Path.GetExtension(transcriptPath);

        var candidate = Path.Combine(dir, $"{stem}.old{ext}");
        if (!File.Exists(candidate)) return candidate;

        for (var i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{stem}.old.{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
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
