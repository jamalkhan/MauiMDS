using MauiMds.AudioCapture;
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

    /// <summary>
    /// Creates a live transcription session for the given engine, or returns null if the
    /// engine does not support live transcription. <paramref name="nativeMicSource"/> enables
    /// native buffer streaming for engines that support it (e.g. Apple Speech on Mac).
    /// </summary>
    ILiveTranscriptionSession? CreateLiveSession(
        TranscriptionEngineType engine,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        INativeMicrophoneSource? nativeMicSource = null);

    IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines { get; }
    IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines { get; }
}
