using MauiMds.Models;

namespace MauiMds.Transcription;

public interface ITranscriptionPipelineFactory
{
    ITranscriptionPipeline Create(
        TranscriptionEngineType engine,
        DiarizationEngineType diarization,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        string pyannotePythonPath = "",
        string pyannoteHfToken = "");

    IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines { get; }
    IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines { get; }
}
