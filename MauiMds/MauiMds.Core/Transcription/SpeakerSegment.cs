namespace MauiMds.Transcription;

public sealed class SpeakerSegment
{
    public string SpeakerLabel { get; init; } = string.Empty;
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
}
