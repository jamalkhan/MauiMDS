namespace MauiMds.Transcription;

/// <summary>
/// Identifies who spoke when in an audio file without performing speech-to-text.
/// Returns a list of time-stamped speaker segments that is subsequently merged with
/// transcription output by <see cref="ISpeakerMergeStrategy"/> to produce a labelled transcript.
/// </summary>
/// <remarks>
/// Speaker segments may overlap (e.g. cross-talk) and may cover only a fraction of the
/// audio (silence or noise is not labelled). The returned labels are opaque strings such as
/// "SPEAKER_00"; human-readable names must be applied at a higher layer.
/// </remarks>
public interface IDiarizationEngine
{
    /// <summary>Human-readable engine name, used in log output and transcript metadata.</summary>
    string Name { get; }

    /// <summary>
    /// <see langword="true"/> when the engine's prerequisites are satisfied and it can run.
    /// Checked at factory time; callers should not invoke <see cref="DiarizeAsync"/> when false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Analyses <paramref name="audioFilePath"/> and returns speaker-labelled time spans,
    /// ordered by <see cref="SpeakerSegment.Start"/>.
    /// </summary>
    /// <param name="audioFilePath">Path to the audio file to analyse.</param>
    /// <param name="progress">Optional progress sink in [0, 1].</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Speaker segments in chronological order.
    /// An empty list means the engine ran but detected no distinct speakers.
    /// </returns>
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
