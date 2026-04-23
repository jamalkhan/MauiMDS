namespace MauiMds.Transcription;

/// <summary>
/// Adapter interface for transcription engines (Apple Speech, Whisper.cpp, etc.).
/// Converts an audio file into timestamped text segments.
/// </summary>
public interface ITranscriptionEngine
{
    string Name { get; }

    /// <summary>False when the engine's runtime dependency is missing or not yet implemented.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
