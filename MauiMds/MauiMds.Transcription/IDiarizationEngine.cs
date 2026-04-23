namespace MauiMds.Transcription;

/// <summary>
/// Adapter interface for speaker diarization engines (pyannote, etc.).
/// Segments audio by speaker without transcribing — the pipeline aligns
/// these segments onto transcription output by timestamp overlap.
/// </summary>
public interface IDiarizationEngine
{
    string Name { get; }

    /// <summary>False when the engine's runtime dependency is missing or not yet implemented.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
