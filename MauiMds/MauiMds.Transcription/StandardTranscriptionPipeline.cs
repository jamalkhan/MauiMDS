using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription;

/// <summary>
/// Runs transcription then diarization and merges the results.
/// Transcript segments are labelled by finding the diarization speaker
/// with the greatest time overlap for each segment.
/// </summary>
internal sealed class StandardTranscriptionPipeline : ITranscriptionPipeline
{
    private readonly ITranscriptionEngine _transcription;
    private readonly IDiarizationEngine _diarization;
    private readonly ILogger _logger;

    public string TranscriptionEngineName => _transcription.Name;
    public string DiarizationEngineName => _diarization.Name;

    public StandardTranscriptionPipeline(
        ITranscriptionEngine transcription,
        IDiarizationEngine diarization,
        ILogger logger)
    {
        _transcription = transcription;
        _diarization = diarization;
        _logger = logger;
    }

    public async Task<TranscriptDocument> RunAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Pipeline starting: transcription={T}, diarization={D}",
            _transcription.Name, _diarization.Name);

        // Split overall progress: 60% transcription, 30% diarization, 10% merge.
        var transcriptionProgress = progress is null ? null
            : new Progress<double>(v => progress.Report(v * 0.6));

        var transcriptSegments = await _transcription.TranscribeAsync(
            audioFilePath, transcriptionProgress, cancellationToken);

        _logger.LogInformation("Transcription complete: {Count} segments.", transcriptSegments.Count);

        var diarizationProgress = progress is null ? null
            : new Progress<double>(v => progress.Report(0.6 + v * 0.3));

        var speakerSegments = await _diarization.DiarizeAsync(
            audioFilePath, diarizationProgress, cancellationToken);

        _logger.LogInformation("Diarization complete: {Count} speaker segments.", speakerSegments.Count);

        var merged = speakerSegments.Count == 0
            ? transcriptSegments
            : MergeByOverlap(transcriptSegments, speakerSegments);

        progress?.Report(1.0);

        var duration = merged.Count > 0 ? merged[^1].End : TimeSpan.Zero;

        return new TranscriptDocument
        {
            Segments = merged,
            AudioFilePath = audioFilePath,
            Duration = duration,
            TranscriptionEngineName = _transcription.Name,
            DiarizationEngineName = _diarization.Name
        };
    }

    /// <summary>
    /// Assigns each transcript segment the speaker label from the diarization
    /// segment that has the greatest temporal overlap with it.
    /// </summary>
    private static IReadOnlyList<TranscriptSegment> MergeByOverlap(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<SpeakerSegment> speakers)
    {
        var result = new List<TranscriptSegment>(segments.Count);

        foreach (var seg in segments)
        {
            var bestLabel = FindBestSpeaker(seg.Start, seg.End, speakers);
            result.Add(new TranscriptSegment
            {
                SpeakerLabel = bestLabel ?? seg.SpeakerLabel,
                Text = seg.Text,
                Start = seg.Start,
                End = seg.End,
                Confidence = seg.Confidence
            });
        }

        return result;
    }

    private static string? FindBestSpeaker(
        TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSegment> speakers)
    {
        string? best = null;
        var bestOverlap = TimeSpan.Zero;

        foreach (var sp in speakers)
        {
            var overlapStart = start > sp.Start ? start : sp.Start;
            var overlapEnd = end < sp.End ? end : sp.End;
            var overlap = overlapEnd - overlapStart;

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = sp.SpeakerLabel;
            }
        }

        return best;
    }
}
