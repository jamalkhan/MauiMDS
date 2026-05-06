namespace MauiMds.Transcription;

public sealed class TranscriptDocument
{
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
    public string AudioFilePath { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration { get; init; }
    public string TranscriptionEngineName { get; init; } = string.Empty;
    public string DiarizationEngineName { get; init; } = string.Empty;
}
