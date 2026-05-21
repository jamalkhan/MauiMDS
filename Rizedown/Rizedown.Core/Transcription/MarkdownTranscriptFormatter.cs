using Rizedown.AudioCapture;
using Rizedown.Models;
using System.Text.RegularExpressions;

namespace Rizedown.Transcription;

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

    // Matches:  > *[HH:mm:ss – HH:mm:ss]* text
    // The separator is an en-dash (U+2013) with surrounding spaces.
    private static readonly Regex SegmentLinePattern = new(
        @"^> \*\[(\d{2}:\d{2}:\d{2}) – (\d{2}:\d{2}:\d{2})\]\* (.+)$",
        RegexOptions.Compiled);

    public IReadOnlyList<TranscriptSegment> ParseSegments(string markdownContent, DateTime? recordingStart)
    {
        var result = new List<TranscriptSegment>();
        var currentSpeaker = "Unknown Speaker";

        foreach (var line in markdownContent.AsSpan().EnumerateLines())
        {
            var lineStr = line.ToString();

            // Speaker header: ### Speaker Name
            if (lineStr.StartsWith("### ", StringComparison.Ordinal))
            {
                currentSpeaker = lineStr[4..].Trim();
                continue;
            }

            var match = SegmentLinePattern.Match(lineStr);
            if (!match.Success) continue;

            var t0 = TimeSpan.Parse(match.Groups[1].Value);
            var t1 = TimeSpan.Parse(match.Groups[2].Value);
            var text = match.Groups[3].Value.Trim();

            // Wall-clock → relative conversion when recording start is known.
            if (recordingStart is { } rs)
            {
                var startOfDay = rs.TimeOfDay;
                t0 = t0 >= startOfDay ? t0 - startOfDay : t0 + (TimeSpan.FromDays(1) - startOfDay);
                t1 = t1 >= startOfDay ? t1 - startOfDay : t1 + (TimeSpan.FromDays(1) - startOfDay);
            }

            result.Add(new TranscriptSegment
            {
                SpeakerLabel = currentSpeaker,
                Text         = text,
                Start        = t0,
                End          = t1,
            });
        }

        return result;
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
