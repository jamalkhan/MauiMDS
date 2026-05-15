namespace Rizedown.Transcription;

/// <summary>
/// A fully assembled transcription pipeline that accepts an audio file and returns a
/// complete <see cref="TranscriptDocument"/> including speaker labels.
/// </summary>
/// <remarks>
/// The pipeline internally sequences transcription and diarization; callers do not need
/// to orchestrate those phases individually. Progress is reported across the combined
/// operation: roughly 0–0.6 for transcription, 0.6–0.9 for diarization, 0.9–1.0 for merge.
/// </remarks>
public interface ITranscriptionPipeline
{
    /// <summary>Human-readable name of the underlying transcription engine.</summary>
    string TranscriptionEngineName { get; }

    /// <summary>
    /// Human-readable name of the underlying diarization engine.
    /// <c>"None"</c> when diarization is disabled.
    /// </summary>
    string DiarizationEngineName { get; }

    /// <summary>
    /// Transcribes and diarizes <paramref name="audioFilePath"/>, returning a merged document.
    /// </summary>
    /// <param name="audioFilePath">Path to the audio file to process (WAV, M4A, MP3, or FLAC).</param>
    /// <param name="progress">Optional progress sink in [0, 1] across both pipeline phases.</param>
    /// <param name="cancellationToken">
    /// Cancels whichever pipeline phase is currently running.
    /// </param>
    /// <returns>
    /// A <see cref="TranscriptDocument"/> whose <see cref="TranscriptDocument.Segments"/>
    /// are ordered by start time and include speaker labels when diarization is enabled.
    /// </returns>
    Task<TranscriptDocument> RunAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
