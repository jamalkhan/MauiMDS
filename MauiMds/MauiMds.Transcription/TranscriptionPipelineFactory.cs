using MauiMds.Models;
#if MACCATALYST
using MauiMds.Transcription.Engines.AppleSpeech;
#endif
using MauiMds.Transcription.Engines.NoOp;
using MauiMds.Transcription.Engines.Pyannote;
using MauiMds.Transcription.Engines.WhisperCpp;
using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription;

/// <summary>
/// Assembles a <see cref="ITranscriptionPipeline"/> from the engine types selected in
/// preferences, injecting the caller-supplied runtime paths into each engine.
/// </summary>
public sealed class TranscriptionPipelineFactory : ITranscriptionPipelineFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TranscriptionPipelineFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    // Used by the Preferences UI to enumerate engine names and availability.
    // Engines are constructed with empty paths here — IsAvailable reflects
    // whether the binary/model exist once the user has configured them.
    public IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines =>
    [
#if MACCATALYST
        new AppleSpeechTranscriptionEngine(
            _loggerFactory.CreateLogger<AppleSpeechTranscriptionEngine>()),
#endif
        new WhisperCppTranscriptionEngine(
            string.Empty, string.Empty,
            _loggerFactory.CreateLogger<WhisperCppTranscriptionEngine>())
    ];

    public IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines =>
    [
        new NoDiarizationEngine(),
        new PyannoteDiarizationEngine(
            string.Empty,
            string.Empty,
            _loggerFactory.CreateLogger<PyannoteDiarizationEngine>())
    ];

    public ITranscriptionPipeline Create(
        TranscriptionEngineType engine,
        DiarizationEngineType diarization,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        string pyannotePythonPath = "",
        string pyannoteHfToken = "")
    {
        var transcriptionEngine = engine switch
        {
#if MACCATALYST
            TranscriptionEngineType.AppleSpeech =>
                (ITranscriptionEngine)new AppleSpeechTranscriptionEngine(
                    _loggerFactory.CreateLogger<AppleSpeechTranscriptionEngine>()),
#endif
            TranscriptionEngineType.WhisperCpp =>
                new WhisperCppTranscriptionEngine(
                    whisperBinaryPath, whisperModelPath,
                    _loggerFactory.CreateLogger<WhisperCppTranscriptionEngine>()),
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };

        var diarizationEngine = diarization switch
        {
            DiarizationEngineType.None =>
                (IDiarizationEngine)new NoDiarizationEngine(),
            DiarizationEngineType.Pyannote =>
                new PyannoteDiarizationEngine(
                    pyannotePythonPath,
                    pyannoteHfToken,
                    _loggerFactory.CreateLogger<PyannoteDiarizationEngine>()),
            _ => throw new ArgumentOutOfRangeException(nameof(diarization), diarization, null)
        };

        return new StandardTranscriptionPipeline(
            transcriptionEngine,
            diarizationEngine,
            _loggerFactory.CreateLogger<StandardTranscriptionPipeline>());
    }
}
