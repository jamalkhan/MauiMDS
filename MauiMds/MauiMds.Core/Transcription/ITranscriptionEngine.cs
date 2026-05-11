namespace MauiMds.Transcription;

/// <summary>
/// Converts speech in an audio file to a flat, time-stamped list of text segments.
/// Implementations perform only speech-to-text — speaker identity is not assigned here;
/// that is the responsibility of <see cref="IDiarizationEngine"/> combined with
/// <see cref="ISpeakerMergeStrategy"/>.
/// </summary>
/// <remarks>
/// Implementations must be safe to call concurrently on different audio files.
/// They are typically long-running (seconds to minutes) and should honour cancellation.
/// </remarks>
public interface ITranscriptionEngine
{
    /// <summary>Human-readable engine name, used in log output and transcript metadata.</summary>
    string Name { get; }

    /// <summary>
    /// <see langword="true"/> when the engine's prerequisites are satisfied and it can run.
    /// For example, <see langword="false"/> when a required binary or model file is missing.
    /// Checked at factory time; callers should not invoke <see cref="TranscribeAsync"/> when false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Transcribes speech in <paramref name="audioFilePath"/> and returns the segments in
    /// chronological order. Segments are recording-relative (start offset from the beginning
    /// of the file, not wall-clock time).
    /// </summary>
    /// <param name="audioFilePath">Path to the audio file to transcribe (WAV, M4A, MP3, or FLAC).</param>
    /// <param name="progress">
    /// Optional progress sink. Values are in [0, 1]; may not be strictly monotonic for all engines.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the operation. Implementations should propagate <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// The recognised segments, ordered by <see cref="TranscriptSegment.Start"/>.
    /// An empty list means no speech was detected, not a failure.
    /// </returns>
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
