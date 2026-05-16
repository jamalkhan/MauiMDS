#if MACCATALYST
using Foundation;
using Rizedown.AudioCapture;
using Microsoft.Extensions.Logging;
using Speech;

namespace Rizedown.Transcription.Engines.AppleSpeech;

/// <summary>
/// Live transcription session backed by Apple's SFSpeechRecognizer.
/// Processes WAV chunks emitted by LiveAudioChunkWriter as standalone
/// SFSpeechUrlRecognitionRequests, giving per-chunk (~20 s) latency.
/// </summary>
internal sealed class AppleSpeechLiveSession : ILiveTranscriptionSession
{
    private readonly SFSpeechRecognizer _recognizer;
    private readonly ILogger _logger;
    private readonly List<Task> _pendingChunks = [];
    private readonly object _pendingLock = new();
    private bool _disposed;

    public event EventHandler<IReadOnlyList<TranscriptSegment>>? SegmentsReady;

    internal AppleSpeechLiveSession(
        SFSpeechRecognizer recognizer,
        INativeMicrophoneSource? _,
        ILogger logger)
    {
        _recognizer = recognizer;
        _logger = logger;
    }

    // ── Chunk path ────────────────────────────────────────────────────────────

    public Task FeedChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct = default)
    {
        if (_disposed) return Task.CompletedTask;
        if (!File.Exists(wavChunkPath)) return Task.CompletedTask;

        var chunkTask = RecognizeChunkAsync(wavChunkPath, chunkStartOffset, ct);

        lock (_pendingLock)
            _pendingChunks.Add(chunkTask);

        // Remove from pending list when done (success or failure).
        _ = chunkTask.ContinueWith(_ =>
        {
            lock (_pendingLock)
                _pendingChunks.Remove(chunkTask);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return chunkTask;
    }

    // FlushAsync waits for all in-flight chunk recognitions to complete so that
    // FinalizeRecordingAsync captures every segment before snapshotting _liveSegments.
    public async Task FlushAsync(CancellationToken ct = default)
    {
        Task[] pending;
        lock (_pendingLock)
            pending = [.. _pendingChunks];

        if (pending.Length == 0) return;

        _logger.LogInformation("AppleSpeechLiveSession: waiting for {Count} in-flight chunk(s) to complete.", pending.Length);
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("AppleSpeechLiveSession: FlushAsync timed out waiting for chunks.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AppleSpeechLiveSession: a chunk failed during flush.");
        }
    }

    private async Task RecognizeChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("AppleSpeechLiveSession: processing chunk at offset {Offset}", chunkStartOffset);

            var fileUrl = NSUrl.FromFilename(wavChunkPath);
            var request = new SFSpeechUrlRecognitionRequest(fileUrl)
            {
                RequiresOnDeviceRecognition = _recognizer.SupportsOnDeviceRecognition,
                ShouldReportPartialResults = false,
                AddsPunctuation = true
            };

            var tcs = new TaskCompletionSource<IReadOnlyList<TranscriptSegment>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _recognizer.GetRecognitionTask(request, (result, error) =>
            {
                if (error is not null)
                {
                    tcs.TrySetException(new NSErrorException(error));
                    return;
                }
                if (result is null || !result.Final) return;

                tcs.TrySetResult(AppleSpeechTranscriptionEngine.ConvertSegmentsPublic(result, chunkStartOffset));
            });

            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var segments = await tcs.Task;
            _logger.LogInformation("AppleSpeechLiveSession: chunk at {Offset} → {Count} segments.", chunkStartOffset, segments.Count);
            if (segments.Count > 0)
                SegmentsReady?.Invoke(this, segments);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppleSpeechLiveSession: chunk recognition failed at offset {Offset}", chunkStartOffset);
        }
        finally
        {
            try { if (File.Exists(wavChunkPath)) File.Delete(wavChunkPath); } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
#endif
