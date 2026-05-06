namespace MauiMds.Transcription;

public sealed class TranscriptSegment
{
    public string SpeakerLabel { get; init; } = "Unknown Speaker";
    public string Text { get; init; } = string.Empty;
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public float Confidence { get; init; } = 1f;
}
