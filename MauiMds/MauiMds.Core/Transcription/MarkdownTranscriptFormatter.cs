using MauiMds.AudioCapture;
using MauiMds.Models;

namespace MauiMds.Transcription;

public sealed class MarkdownTranscriptFormatter : ITranscriptFormatter
{
    public string FormatLiveProgress(
        RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments)
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

    public string FormatFinalLiveTranscript(
        RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments)
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

    public string FormatBatchProgress(DateTime startedAt, IList<string> progressRows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Audio File Found. Beginning Transcription.");
        sb.AppendLine();
        sb.AppendLine($"* {startedAt:yyyy-MM-dd HH:mm:ss} Transcription Started");
        foreach (var row in progressRows)
            sb.AppendLine(row);
        return sb.ToString();
    }

    public string FormatGroupTranscript(RecordingGroup group, IEnumerable<TranscriptSegment> segments)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Transcript: {group.DisplayName}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        RecordingPathBuilder.TryParseRecordingStart(group.BaseName, out var recordingStart);
        AppendSpeakerGroupedSegments(sb,
            segments.Select(s => (s.Start, s.End, s.SpeakerLabel ?? "Speaker", s.Text)),
            recordingStart == default ? null : recordingStart);
        return sb.ToString();
    }

    public string FormatDiarizedTranscript(
        RecordingGroup group, IReadOnlyList<TranscriptSegment> segments, TranscriptDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Transcript: {group.DisplayName}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Engine: {doc.TranscriptionEngineName} | {doc.DiarizationEngineName}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        RecordingPathBuilder.TryParseRecordingStart(group.BaseName, out var recordingStart);
        AppendSpeakerGroupedSegments(sb,
            segments.Select(s => (s.Start, s.End, s.SpeakerLabel ?? "Speaker", s.Text)),
            recordingStart == default ? null : recordingStart);
        return sb.ToString();
    }

    public string FormatSingleFileTranscript(TranscriptDocument doc, string audioPath)
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
}
