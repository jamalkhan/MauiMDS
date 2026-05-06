#if MACCATALYST
using Foundation;
using MauiMds.AudioCapture;
using Microsoft.Extensions.Logging;
using Speech;

namespace MauiMds.Transcription.Engines.AppleSpeech;

/// <summary>
/// Live transcription session backed by Apple's SFSpeechRecognizer.
/// Processes WAV chunks emitted by LiveAudioChunkWriter as standalone
/// SFSpeechUrlRecognitionRequests, giving per-chunk (~20 s) latency.
/// </summary>
internal sealed class AppleSpeechLiveSession : ILiveTranscriptionSession
{
    private readonly SFSpeechRecognizer _recognizer;
    private readonly ILogger _logger;
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

    public async Task FeedChunkAsync(string wavChunkPath, TimeSpan chunkStartOffset, CancellationToken ct = default)
    {
        if (_disposed) return;

        if (!File.Exists(wavChunkPath)) return;

        try
        {
            _logger.LogDebug("AppleSpeechLiveSession: processing chunk at offset {Offset}", chunkStartOffset);

            var fileUrl = NSUrl.FromFilename(wavChunkPath);
            var request = new SFSpeechUrlRecognitionRequest(fileUrl)
            {
                RequiresOnDeviceRecognition = true,
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
            if (segments.Count > 0)
                SegmentsReady?.Invoke(this, segments);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AppleSpeechLiveSession: chunk recognition failed");
        }
        finally
        {
            try { if (File.Exists(wavChunkPath)) File.Delete(wavChunkPath); } catch { }
        }
    }

    public Task FlushAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
#endif
