namespace MauiMds.Transcription;

public interface ITranscriptionEngine
{
    string Name { get; }
    bool IsAvailable { get; }

    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
