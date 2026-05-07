namespace MauiMds.Transcription;

public interface ISpeakerMergeStrategy
{
    IReadOnlyList<TranscriptSegment> Merge(
        IReadOnlyList<TranscriptSegment> source,
        IReadOnlyList<SpeakerSegment> speakers);
}
