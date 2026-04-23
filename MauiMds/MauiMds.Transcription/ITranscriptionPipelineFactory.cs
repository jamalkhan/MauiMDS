using MauiMds.Models;

namespace MauiMds.Transcription;

/// <summary>
/// Abstract factory that assembles a <see cref="ITranscriptionPipeline"/> from the engine
/// types chosen in preferences. Callers never instantiate engines directly.
/// </summary>
public interface ITranscriptionPipelineFactory
{
    ITranscriptionPipeline Create(
        TranscriptionEngineType engine,
        DiarizationEngineType diarization,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        string pyannotePythonPath = "");

    IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines { get; }
    IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines { get; }
}
