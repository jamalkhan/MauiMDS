namespace MauiMds.Transcription;

/// <summary>
/// An ongoing live transcription session that accepts audio chunks and emits segments
/// incrementally. Supported by both WhisperCpp (chunk-based WAV files) and Apple Speech
/// (native buffer streaming). The caller feeds chunks via FeedChunkAsync and calls
/// FlushAsync when the audio source is done.
/// </summary>
public interface ILiveTranscriptionSession : IAsyncDisposable
{
    event EventHandler<IReadOnlyList<TranscriptSegment>>? SegmentsReady;

    /// <summary>
    /// Feed a WAV audio chunk to the session. <paramref name="chunkStartOffset"/> is the
    /// position of the start of this chunk within the overall recording, used to shift
    /// the segment timestamps so they are recording-relative rather than chunk-relative.
    /// </summary>
    Task FeedChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct = default);

    /// <summary>Flush any buffered audio and emit final segments. Call once when recording stops.</summary>
    Task FlushAsync(CancellationToken ct = default);
}
