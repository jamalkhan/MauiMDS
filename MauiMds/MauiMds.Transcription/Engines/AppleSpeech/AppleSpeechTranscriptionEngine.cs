using Foundation;
using MauiMds.AudioCapture;
using Speech;
using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription.Engines.AppleSpeech;

/// <summary>
/// Transcription adapter backed by Apple's SFSpeechRecognizer.
/// Uses on-device recognition — no audio leaves the machine.
/// </summary>
public sealed class AppleSpeechTranscriptionEngine : ITranscriptionEngine
{
    private readonly ILogger _logger;

    public string Name => "Apple Speech Framework";

    public bool IsAvailable
    {
        get
        {
            var recognizer = new SFSpeechRecognizer();
            return recognizer.Available && recognizer.SupportsOnDeviceRecognition;
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

        var fileUrl = NSUrl.FromFilename(audioFilePath);
        var request = new SFSpeechUrlRecognitionRequest(fileUrl)
        {
            RequiresOnDeviceRecognition = true,
            ShouldReportPartialResults = false,
            // Add punctuation if the engine supports it (macOS 13+).
            AddsPunctuation = true
        };

        _logger.LogInformation("AppleSpeech: starting recognition on {File}.", audioFilePath);
        progress?.Report(0.05);

        var segments = await RecognizeAsync(recognizer, request, cancellationToken);

        progress?.Report(1.0);
        _logger.LogInformation("AppleSpeech: recognition complete — {Count} segments.", segments.Count);
        return segments;
    }

    private static Task<SFSpeechRecognizerAuthorizationStatus> RequestAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        SFSpeechRecognizer.RequestAuthorization(status => tcs.TrySetResult(status));
        return tcs.Task;
    }

    private Task<IReadOnlyList<TranscriptSegment>> RecognizeAsync(
        SFSpeechRecognizer recognizer,
        SFSpeechUrlRecognitionRequest request,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<TranscriptSegment>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        SFSpeechRecognitionTask? task = null;

        task = recognizer.GetRecognitionTask(request, (result, error) =>
        {
            if (error is not null)
            {
                _logger.LogError("AppleSpeech recognition error: {Error}", error.LocalizedDescription);
                tcs.TrySetException(new NSErrorException(error));
                return;
            }

            if (result is null || !result.Final)
            {
                return;
            }

            var segments = ConvertSegments(result);
            tcs.TrySetResult(segments);
        });

        cancellationToken.Register(() =>
        {
            task?.Cancel();
            tcs.TrySetCanceled(cancellationToken);
        });

        return tcs.Task;
    }

    // Called from AppleSpeechLiveSession for both native streaming results and chunk results.
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
        var buffer = new System.Text.StringBuilder();
        var groupStart = TimeSpan.Zero;
        var prevEnd = TimeSpan.Zero;
        float minConfidence = 1f;

        foreach (var seg in rawSegments)
        {
            var start = TimeSpan.FromSeconds(seg.Timestamp);
            var end = start + TimeSpan.FromSeconds(seg.Duration);

            if (buffer.Length > 0 && (start - prevEnd).TotalSeconds > maxGapSeconds)
            {
                grouped.Add(new TranscriptSegment
                {
                    Text = buffer.ToString().Trim(),
                    Start = groupStart + startOffset,
                    End = prevEnd + startOffset,
                    Confidence = minConfidence
                });
                buffer.Clear();
                minConfidence = 1f;
                groupStart = start;
            }

            if (buffer.Length == 0)
            {
                groupStart = start;
            }

            buffer.Append(seg.Substring);
            buffer.Append(' ');
            prevEnd = end;
            if (seg.Confidence < minConfidence)
            {
                minConfidence = seg.Confidence;
            }
        }

        if (buffer.Length > 0)
        {
            grouped.Add(new TranscriptSegment
            {
                Text = buffer.ToString().Trim(),
                Start = groupStart + startOffset,
                End = prevEnd + startOffset,
                Confidence = minConfidence
            });
        }

        return grouped;
    }
}
