namespace MauiMds.Transcription;

/// <summary>
/// An ongoing live transcription session that accepts audio chunks and emits segments
/// incrementally. Supported by WhisperCpp (chunk-based WAV files) and Apple Speech
/// (native buffer streaming). The caller feeds chunks via <see cref="FeedChunkAsync"/>
/// and calls <see cref="FlushAsync"/> once when the audio source is done.
/// </summary>
/// <remarks>
/// Event subscriptions to <see cref="SegmentsReady"/> must be removed before calling
/// <see cref="IAsyncDisposable.DisposeAsync"/> to avoid stale handler references.
/// </remarks>
public interface ILiveTranscriptionSession : IAsyncDisposable
{
    /// <summary>
    /// Raised on a background thread each time a new batch of segments becomes available.
    /// Segments in each batch are recording-relative (start/end offsets from the beginning
    /// of the recording, not from the chunk boundary). Accumulate across batches for the full
    /// running transcript.
    /// </summary>
    event EventHandler<IReadOnlyList<TranscriptSegment>>? SegmentsReady;

    /// <summary>
    /// Feed a WAV audio chunk to the session. <paramref name="chunkStartOffset"/> is the
    /// position of the start of this chunk within the overall recording, used to shift
    /// segment timestamps so they are recording-relative rather than chunk-relative.
    /// </summary>
    Task FeedChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct = default);

    /// <summary>
    /// Flush any buffered audio and emit final segments. Call exactly once after the last
    /// <see cref="FeedChunkAsync"/> call, before <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);
}
