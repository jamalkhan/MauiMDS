using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription.Engines.WhisperCpp;

/// <summary>
/// Live transcription session that processes WAV audio chunks sequentially via whisper-cli.
/// Each chunk is a complete WAV file produced by the audio capture service at a regular interval.
///
/// If a new chunk arrives while whisper is still processing the previous one, it replaces the
/// queued-but-not-yet-started chunk rather than being dropped or queued unboundedly. This keeps
/// latency bounded: the model always works on the most recent audio available.
/// </summary>
internal sealed class WhisperCppLiveSession : ILiveTranscriptionSession
{
    private readonly WhisperCppTranscriptionEngine _engine;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _workerGate = new(1, 1);

    // Pending slot: holds the next chunk to process. Written by callers, read by the worker.
    private (string Path, TimeSpan Offset)? _pending;
    private readonly object _pendingLock = new();

    private bool _disposed;

    public event EventHandler<IReadOnlyList<TranscriptSegment>>? SegmentsReady;

    internal WhisperCppLiveSession(WhisperCppTranscriptionEngine engine, ILogger logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task FeedChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct = default)
    {
        if (_disposed) return;
        if (!File.Exists(wavChunkPath))
        {
            _logger.LogWarning("WhisperCppLiveSession: chunk file not found — {Path}", wavChunkPath);
            return;
        }

        string? displaced;
        lock (_pendingLock)
        {
            displaced = _pending?.Path;
            _pending = (wavChunkPath, chunkStartOffset);
        }

        // Discard any chunk we just replaced — it will never be processed.
        if (displaced is not null && displaced != wavChunkPath)
        {
            _logger.LogDebug("WhisperCppLiveSession: replacing queued chunk with newer one");
            TryDeleteChunk(displaced);
        }

        // If the worker is already running it will pick up _pending when it finishes.
        if (!await _workerGate.WaitAsync(0, ct)) return;

        try
        {
            while (true)
            {
                (string path, TimeSpan offset) chunk;
                lock (_pendingLock)
                {
                    if (_pending is null) break;
                    chunk = _pending.Value;
                    _pending = null;
                }

                await ProcessChunkAsync(chunk.path, chunk.offset, ct);
            }
        }
        finally
        {
            _workerGate.Release();
        }
    }

    private async Task ProcessChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("WhisperCppLiveSession: processing chunk at offset {Offset}", chunkStartOffset);
            var segments = await _engine.TranscribeAsync(wavChunkPath, progress: null, ct);
            if (segments.Count == 0) return;

            var shifted = segments
                .Select(s => new TranscriptSegment
                {
                    Text = s.Text,
                    Start = s.Start + chunkStartOffset,
                    End = s.End + chunkStartOffset,
                    Confidence = s.Confidence,
                    SpeakerLabel = s.SpeakerLabel
                })
                .ToList();

            SegmentsReady?.Invoke(this, shifted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhisperCppLiveSession: chunk transcription failed");
        }
        finally
        {
            TryDeleteChunk(wavChunkPath);
        }
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _workerGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static void TryDeleteChunk(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
