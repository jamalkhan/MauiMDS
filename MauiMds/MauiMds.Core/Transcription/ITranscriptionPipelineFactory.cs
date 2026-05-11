using MauiMds.AudioCapture;
using MauiMds.Models;

namespace MauiMds.Transcription;

/// <summary>
/// Constructs transcription pipelines and live sessions from user-facing engine selections.
/// The factory owns the registry of available engines; call <see cref="AvailableTranscriptionEngines"/>
/// and <see cref="AvailableDiarizationEngines"/> to populate preference UI lists.
/// </summary>
public interface ITranscriptionPipelineFactory
{
    /// <summary>
    /// Builds a batch <see cref="ITranscriptionPipeline"/> for the given engine combination.
    /// The returned pipeline is not thread-safe and should not be shared across concurrent calls.
    /// </summary>
    /// <param name="engine">Speech-to-text engine to use.</param>
    /// <param name="diarization">Speaker diarization engine, or <see cref="DiarizationEngineType.None"/>.</param>
    /// <param name="whisperBinaryPath">Path to the <c>whisper-cli</c> binary; only required when <paramref name="engine"/> is Whisper.</param>
    /// <param name="whisperModelPath">Path to the GGML model file; only required when <paramref name="engine"/> is Whisper.</param>
    /// <param name="pyannotePythonPath">Path to the Python interpreter in the pyannote virtual environment.</param>
    /// <param name="pyannoteHfToken">Hugging Face token with access to the pyannote model.</param>
    ITranscriptionPipeline Create(
        TranscriptionEngineType engine,
        DiarizationEngineType diarization,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        string pyannotePythonPath = "",
        string pyannoteHfToken = "");

    /// <summary>
    /// Creates a live transcription session for the given engine, or returns <see langword="null"/>
    /// if the engine does not support live transcription. <paramref name="nativeMicSource"/>
    /// enables native buffer streaming for engines that support it (e.g. Apple Speech on Mac),
    /// bypassing chunk-file I/O.
    /// </summary>
    ILiveTranscriptionSession? CreateLiveSession(
        TranscriptionEngineType engine,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        INativeMicrophoneSource? nativeMicSource = null);

    /// <summary>All transcription engines registered in this factory instance.</summary>
    IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines { get; }

    /// <summary>All diarization engines registered in this factory instance.</summary>
    IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines { get; }
}
