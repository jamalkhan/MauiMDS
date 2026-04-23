namespace MauiMds.Transcription;

/// <summary>Represents a time range attributed to a single speaker by a diarization engine.</summary>
public sealed class SpeakerSegment
{
    public string SpeakerLabel { get; init; } = string.Empty;
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
}
