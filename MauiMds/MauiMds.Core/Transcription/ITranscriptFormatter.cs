using MauiMds.Models;

namespace MauiMds.Transcription;

public interface ITranscriptFormatter
{
    string FormatLiveProgress(RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments);
    string FormatFinalLiveTranscript(RecordingGroup group, DateTime startedAt, IReadOnlyList<TranscriptSegment> segments);
    string FormatBatchProgress(DateTime startedAt, IList<string> progressRows);
    string FormatGroupTranscript(RecordingGroup group, IEnumerable<TranscriptSegment> segments);
    string FormatDiarizedTranscript(RecordingGroup group, IReadOnlyList<TranscriptSegment> segments, TranscriptDocument doc);
    string FormatSingleFileTranscript(TranscriptDocument doc, string audioPath);
}
