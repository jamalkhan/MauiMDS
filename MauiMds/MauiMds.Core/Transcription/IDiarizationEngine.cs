namespace MauiMds.Transcription;

public interface IDiarizationEngine
{
    string Name { get; }
    bool IsAvailable { get; }

    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
