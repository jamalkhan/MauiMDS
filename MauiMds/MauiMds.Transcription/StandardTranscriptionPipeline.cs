using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription;

/// <summary>
/// Runs transcription then diarization and merges the results.
/// Speaker assignment is delegated to the injected <see cref="ISpeakerMergeStrategy"/>.
/// </summary>
internal sealed class StandardTranscriptionPipeline : ITranscriptionPipeline
{
    private readonly ITranscriptionEngine _transcription;
    private readonly IDiarizationEngine _diarization;
    private readonly ISpeakerMergeStrategy _mergeStrategy;
    private readonly ILogger _logger;

    public string TranscriptionEngineName => _transcription.Name;
    public string DiarizationEngineName => _diarization.Name;

    public StandardTranscriptionPipeline(
        ITranscriptionEngine transcription,
        IDiarizationEngine diarization,
        ISpeakerMergeStrategy mergeStrategy,
        ILogger logger)
    {
        _transcription = transcription;
        _diarization = diarization;
        _mergeStrategy = mergeStrategy;
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
            : _mergeStrategy.Merge(transcriptSegments, speakerSegments);

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

}
