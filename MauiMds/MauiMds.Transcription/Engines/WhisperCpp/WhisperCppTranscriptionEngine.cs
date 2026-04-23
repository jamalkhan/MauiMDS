namespace MauiMds.Transcription.Engines.WhisperCpp;

/// <summary>
/// Stub adapter for whisper.cpp local transcription.
/// Will invoke the whisper.cpp binary/library once integration is implemented.
/// </summary>
public sealed class WhisperCppTranscriptionEngine : ITranscriptionEngine
{
    private readonly string _modelPath;

    public string Name => "Whisper.cpp";

    /// <summary>Available only when a model file path has been configured.</summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath);

    public WhisperCppTranscriptionEngine(string modelPath)
    {
        _modelPath = modelPath;
    }

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Whisper.cpp integration is not yet implemented. " +
            "Configure a model path in Preferences > Transcription once it becomes available.");
    }
}
