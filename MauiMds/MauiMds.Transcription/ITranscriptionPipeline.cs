namespace MauiMds.Transcription;

/// <summary>
/// A composed pipeline of one transcription engine and one diarization engine.
/// Produced by <see cref="ITranscriptionPipelineFactory"/>.
/// </summary>
public interface ITranscriptionPipeline
{
    string TranscriptionEngineName { get; }
    string DiarizationEngineName { get; }

    Task<TranscriptDocument> RunAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
