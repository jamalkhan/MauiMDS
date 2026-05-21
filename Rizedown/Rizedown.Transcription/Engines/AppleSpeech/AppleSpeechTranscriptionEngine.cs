using Foundation;
using Rizedown.AudioCapture;
using Rizedown.AudioCapture.MacCatalyst;
using Speech;
using Microsoft.Extensions.Logging;

namespace Rizedown.Transcription.Engines.AppleSpeech;

/// <summary>
/// Transcription adapter backed by Apple's SFSpeechRecognizer.
/// Uses on-device recognition — no audio leaves the machine.
/// </summary>
public sealed class AppleSpeechTranscriptionEngine : ITranscriptionEngine
{
    private readonly ILogger _logger;

    // Apple Speech has an ~1-minute per-request limit; keep chunks well under it.
    private const int ChunkSeconds = 55;

    public string Name => "Apple Speech Framework";

    public bool IsAvailable
    {
        get
        {
            var recognizer = new SFSpeechRecognizer();
            return recognizer.Available;
        }
    }

    public AppleSpeechTranscriptionEngine(ILogger<AppleSpeechTranscriptionEngine> logger)
    {
        _logger = logger;
    }

    public ILiveTranscriptionSession CreateLiveSession(INativeMicrophoneSource? nativeMicSource = null)
    {
        var recognizer = new SFSpeechRecognizer();
        return new AppleSpeechLiveSession(recognizer, nativeMicSource, _logger);
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AppleSpeech: requesting authorization.");
        var authStatus = await RequestAuthorizationAsync();

        if (authStatus != SFSpeechRecognizerAuthorizationStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Speech recognition permission denied ({authStatus}). " +
                "Grant access in System Settings > Privacy & Security > Speech Recognition.");
        }

        var recognizer = new SFSpeechRecognizer();

        if (!recognizer.Available)
        {
            throw new InvalidOperationException(
                "SFSpeechRecognizer is not available on this device.");
        }

        _logger.LogInformation("AppleSpeech: starting chunked batch transcription on {File}.", audioFilePath);
        progress?.Report(0.02);

        var allSegments = new List<TranscriptSegment>();
        int chunkIndex  = 0;

        // Wrap progress: AudioFileChunker reports [0,1]; we map to [0.02, 1.0].
        var innerProgress = progress is null ? null : new Progress<double>(
            p => progress.Report(0.02 + 0.98 * p));

        await AudioFileChunker.ProcessChunksAsync(
            audioFilePath,
            ChunkSeconds,
            async (wavPath, chunkStart, ct) =>
            {
                chunkIndex++;
                _logger.LogInformation("AppleSpeech batch: chunk {I} at {Offset}.", chunkIndex, chunkStart);
                var segments = await RecognizeWavChunkAsync(recognizer, wavPath, chunkStart, ct);
                _logger.LogInformation("AppleSpeech batch: chunk {I} → {Count} segments.", chunkIndex, segments.Count);
                allSegments.AddRange(segments);
            },
            innerProgress,
            cancellationToken);

        _logger.LogInformation("AppleSpeech: recognition complete — {Count} segments.", allSegments.Count);
        return allSegments;
    }

    // ── Chunk recognition ─────────────────────────────────────────────────────

    private async Task<IReadOnlyList<TranscriptSegment>> RecognizeWavChunkAsync(
        SFSpeechRecognizer recognizer,
        string wavPath,
        TimeSpan chunkStartOffset,
        CancellationToken cancellationToken)
    {
        var request = new SFSpeechUrlRecognitionRequest(NSUrl.FromFilename(wavPath))
        {
            RequiresOnDeviceRecognition = recognizer.SupportsOnDeviceRecognition,
            ShouldReportPartialResults  = false,
            AddsPunctuation             = true
        };

        var tcs = new TaskCompletionSource<IReadOnlyList<TranscriptSegment>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        recognizer.GetRecognitionTask(request, (result, error) =>
        {
            if (error is not null)
            {
                if (error.Code == 1110) // No speech detected — not a real error
                    tcs.TrySetResult([]);
                else
                    tcs.TrySetException(new NSErrorException(error));
                return;
            }
            if (result is null || !result.Final) return;
            tcs.TrySetResult(ConvertSegmentsPublic(result, chunkStartOffset));
        });

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    private static Task<SFSpeechRecognizerAuthorizationStatus> RequestAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        SFSpeechRecognizer.RequestAuthorization(status => tcs.TrySetResult(status));
        return tcs.Task;
    }

    // ── Segment conversion ────────────────────────────────────────────────────

    // Called from AppleSpeechLiveSession for chunk results.
    internal static IReadOnlyList<TranscriptSegment> ConvertSegmentsPublic(
        SFSpeechRecognitionResult result,
        TimeSpan startOffset = default)
        => ConvertSegments(result, startOffset);

    private static IReadOnlyList<TranscriptSegment> ConvertSegments(
        SFSpeechRecognitionResult result,
        TimeSpan startOffset = default)
    {
        var rawSegments = result.BestTranscription.Segments;

        // Apple Speech returns word-level segments. Group them into sentence-like
        // chunks by merging words that are close together (gap < 1.5 s).
        const double maxGapSeconds = 1.5;
        var grouped = new List<TranscriptSegment>();
        var buffer  = new System.Text.StringBuilder();
        var groupStart   = TimeSpan.Zero;
        var prevEnd      = TimeSpan.Zero;
        float minConfidence = 1f;

        foreach (var seg in rawSegments)
        {
            var word = seg.Substring?.Trim() ?? string.Empty;
            if (word.Length == 0) continue;

            var start = TimeSpan.FromSeconds(seg.Timestamp);
            var end   = start + TimeSpan.FromSeconds(seg.Duration);

            if (buffer.Length > 0 && (start - prevEnd).TotalSeconds > maxGapSeconds)
            {
                var text = buffer.ToString().Trim();
                if (text.Length > 0)
                    grouped.Add(new TranscriptSegment
                    {
                        Text       = text,
                        Start      = groupStart + startOffset,
                        End        = prevEnd + startOffset,
                        Confidence = minConfidence
                    });
                buffer.Clear();
                minConfidence = 1f;
                groupStart    = start;
            }

            if (buffer.Length == 0)
                groupStart = start;

            buffer.Append(word);
            buffer.Append(' ');
            prevEnd = end;
            if (seg.Confidence < minConfidence)
                minConfidence = seg.Confidence;
        }

        if (buffer.Length > 0)
        {
            var text = buffer.ToString().Trim();
            if (text.Length > 0)
                grouped.Add(new TranscriptSegment
                {
                    Text       = text,
                    Start      = groupStart + startOffset,
                    End        = prevEnd + startOffset,
                    Confidence = minConfidence
                });
        }

        return grouped;
    }
}
