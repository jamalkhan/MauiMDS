namespace MauiMds.Transcription.Engines.Pyannote;

/// <summary>
/// Stub adapter for pyannote.audio speaker diarization.
/// Will call a local Python environment running pyannote.audio 3.1 once implemented.
/// </summary>
public sealed class PyannoteDiarizationEngine : IDiarizationEngine
{
    private readonly string _pythonPath;

    public string Name => "pyannote.audio";

    /// <summary>Available only when a Python executable path has been configured.</summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_pythonPath) && File.Exists(_pythonPath);

    public PyannoteDiarizationEngine(string pythonPath)
    {
        _pythonPath = pythonPath;
    }

    public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "pyannote.audio integration is not yet implemented. " +
            "Configure a Python path in Preferences > Transcription once it becomes available.");
    }
}
