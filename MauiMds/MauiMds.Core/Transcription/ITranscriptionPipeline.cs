namespace MauiMds.Transcription;

public interface ITranscriptionPipeline
{
    string TranscriptionEngineName { get; }
    string DiarizationEngineName { get; }

    Task<TranscriptDocument> RunAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
