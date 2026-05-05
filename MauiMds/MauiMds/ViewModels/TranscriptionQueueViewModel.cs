using MauiMds.AudioCapture;
using MauiMds.Features.Workspace;
using MauiMds.Models;
using MauiMds.Transcription;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MauiMds.ViewModels;

/// <summary>
/// Settings snapshot passed to the transcription pipeline. Populated from the current
/// preferences UI fields so in-progress edits are used without requiring a preferences save.
/// </summary>
public sealed record TranscriptionConfig(
    TranscriptionEngineType Engine,
    DiarizationEngineType Diarization,
    string WhisperBinaryPath,
    string WhisperModelPath,
    string PyannotePythonPath,
    string PyannoteHfToken);

/// <summary>
/// Owns the transcription queue: enqueueing groups after recording, processing them in order,
/// and surfacing per-item state (actively transcribing, queued) back to workspace items.
/// </summary>
public sealed class TranscriptionQueueViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Fire to show a progress document in the editor while transcription runs.</summary>
    public event EventHandler<MarkdownDocument>? LoadDocumentRequested;

    /// <summary>Fire to open the finished transcript in the editor.</summary>
    public event EventHandler<string>? OpenDocumentRequested;

    /// <summary>Fire to update editor text with live transcription progress.</summary>
    public event EventHandler<TranscriptionProgressEventArgs>? EditorProgressUpdated;

    /// <summary>Fire to trigger a workspace refresh after transcription completes.</summary>
    public event EventHandler? WorkspaceRefreshNeeded;

    private readonly ITranscriptionPipelineFactory _pipelineFactory;
    private readonly WorkspaceExplorerState _workspace;
    private readonly ILogger<TranscriptionQueueViewModel> _logger;
    private readonly Func<TranscriptionConfig> _getConfig;
    private readonly Func<RecordingGroup?> _getSelectedGroup;
    private readonly Action<RecordingGroup?> _setSelectedGroup;
    private readonly Func<string, Exception?, string, Task> _reportError;
    private readonly Action<string> _setStatus;

    private readonly List<TranscriptionJob> _jobs = [];
    private readonly CancellationTokenSource _cts = new();
    private bool _isProcessing;

    public TranscriptionQueueViewModel(
        ITranscriptionPipelineFactory pipelineFactory,
        WorkspaceExplorerState workspace,
        ILogger<TranscriptionQueueViewModel> logger,
        Func<TranscriptionConfig> getConfig,
        Func<RecordingGroup?> getSelectedGroup,
        Action<RecordingGroup?> setSelectedGroup,
        Func<string, Exception?, string, Task> reportError,
        Action<string> setStatus)
    {
        _pipelineFactory = pipelineFactory;
        _workspace = workspace;
        _logger = logger;
        _getConfig = getConfig;
        _getSelectedGroup = getSelectedGroup;
        _setSelectedGroup = setSelectedGroup;
        _reportError = reportError;
        _setStatus = setStatus;

        ReTranscribeGroupCommand = new Command(async () => await ReTranscribeGroupAsync());
        TranscribeAudioCommand = new Command<WorkspaceTreeItem>(async item => await TranscribeAudioAsync(item));

        AppDomain.CurrentDomain.ProcessExit += (_, _) => _cts.Cancel();
    }

    public ICommand ReTranscribeGroupCommand { get; }
    public ICommand TranscribeAudioCommand { get; }

    public bool CanReTranscribeGroup =>
        _getSelectedGroup() is { HasTranscript: true } group &&
        !_jobs.Any(j => string.Equals(j.Group.BaseName, group.BaseName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Notifies the view that CanReTranscribeGroup may have changed (e.g. after selection changes).</summary>
    public void NotifyCanReTranscribeGroupChanged()
        => OnPropertyChanged(nameof(CanReTranscribeGroup));

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

    /// <summary>Applies queued/active-transcribing highlights to workspace items.</summary>
    public void ApplyHighlights(IEnumerable<WorkspaceTreeItem> items)
    {
        var activeBaseName = _jobs.FirstOrDefault(j => j.Status == TranscriptionJobStatus.Active)?.Group.BaseName;
        foreach (var item in items)
        {
            if (!item.IsRecordingGroup) continue;
            var baseName = item.RecordingGroup!.BaseName;
            item.IsActivelyTranscribing = string.Equals(baseName, activeBaseName, StringComparison.OrdinalIgnoreCase);
            item.IsInTranscriptionQueue = _jobs.Any(j => j.Status == TranscriptionJobStatus.Queued &&
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
            if (progressRows.Count >= 20) return;
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
                    allSegments.Add((seg.Start, seg.End, "You", seg.Text));
            }

            if (group.SysFilePath is { } sysPath)
            {
                var sysDoc = await pipeline.RunAsync(sysPath, progress, _cts.Token);
                foreach (var seg in sysDoc.Segments)
                    allSegments.Add((seg.Start, seg.End, "System Audio", seg.Text));
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
                MainThread.BeginInvokeOnMainThread(() => _setStatus($"Transcribing… {v:P0}")));

            var doc = await pipeline.RunAsync(audioPath, progress);
            var markdown = BuildTranscriptMarkdown(doc, audioPath);
            await File.WriteAllTextAsync(transcriptPath, markdown);

            _setStatus(string.Empty);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is not null)
                    await page.DisplayAlertAsync("Transcription Complete",
                        $"Transcript saved to:\n{Path.GetFileName(transcriptPath)}", "OK");
            });
        }
        catch (Exception ex)
        {
            await _reportError("Transcription failed.", ex, ex.Message);
        }
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
            doc.Segments.Select(s => (s.Start, s.End, s.SpeakerLabel, s.Text)),
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
                if (currentSpeaker is not null)
                    sb.AppendLine();
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

        if (currentSpeaker is not null)
            sb.AppendLine();
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
        if (App.IsTerminating) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TranscriptionProgressEventArgs : EventArgs
{
    public RecordingGroup Group { get; init; } = null!;
    public string Content { get; init; } = string.Empty;
}

internal enum TranscriptionJobStatus { Queued, Active }

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
