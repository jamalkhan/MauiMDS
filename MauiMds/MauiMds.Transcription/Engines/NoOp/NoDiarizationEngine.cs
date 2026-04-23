namespace MauiMds.Transcription.Engines.NoOp;

/// <summary>
/// Pass-through diarization engine that returns no speaker segments.
/// Transcription segments keep their default "Unknown Speaker" label.
/// </summary>
internal sealed class NoDiarizationEngine : IDiarizationEngine
{
    public string Name => "None";
    public bool IsAvailable => true;

    public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(1.0);
        return Task.FromResult<IReadOnlyList<SpeakerSegment>>([]);
    }
}
