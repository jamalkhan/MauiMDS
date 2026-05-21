using Rizedown.Models;

namespace Rizedown.Transcription;

public interface ITranscriptFormatter
{
    string FormatLiveProgress(RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments);
    string FormatFinalLiveTranscript(RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments);
    string FormatBatchProgress(DateTime startedAt, IList<string> progressRows);
    string FormatGroupTranscript(RecordingGroup group, IEnumerable<TranscriptSegment> segments);
    string FormatDiarizedTranscript(RecordingGroup group, IReadOnlyList<TranscriptSegment> segments, TranscriptDocument doc);
    string FormatSingleFileTranscript(TranscriptDocument doc, string audioPath);

    /// <summary>
    /// Parses a markdown transcript produced by this formatter back into segments.
    /// When <paramref name="recordingStart"/> is provided, wall-clock timestamps in the
    /// file are converted back to recording-relative <see cref="TimeSpan"/> values.
    /// </summary>
    IReadOnlyList<TranscriptSegment> ParseSegments(string markdownContent, DateTime? recordingStart);
}
