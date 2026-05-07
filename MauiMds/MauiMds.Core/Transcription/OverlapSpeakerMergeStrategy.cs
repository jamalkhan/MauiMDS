namespace MauiMds.Transcription;

/// <summary>
/// Assigns each source segment the speaker label from whichever diarization segment has
/// the greatest temporal overlap with it. Falls back to the source segment's existing label
/// when no overlap exists.
/// </summary>
public sealed class OverlapSpeakerMergeStrategy : ISpeakerMergeStrategy
{
    public IReadOnlyList<TranscriptSegment> Merge(
        IReadOnlyList<TranscriptSegment> source,
        IReadOnlyList<SpeakerSegment> speakers)
    {
        var result = new List<TranscriptSegment>(source.Count);
        foreach (var seg in source)
        {
            var bestLabel = FindBestSpeaker(seg.Start, seg.End, speakers);
            result.Add(new TranscriptSegment
            {
                SpeakerLabel = bestLabel ?? seg.SpeakerLabel,
                Text         = seg.Text,
                Start        = seg.Start,
                End          = seg.End,
                Confidence   = seg.Confidence
            });
        }
        return result;
    }

    private static string? FindBestSpeaker(
        TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSegment> speakers)
    {
        string? best = null;
        var bestOverlap = TimeSpan.Zero;

        foreach (var sp in speakers)
        {
            var overlapStart = start > sp.Start ? start : sp.Start;
            var overlapEnd   = end   < sp.End   ? end   : sp.End;
            var overlap = overlapEnd - overlapStart;

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = sp.SpeakerLabel;
            }
        }

        return best;
    }
}
