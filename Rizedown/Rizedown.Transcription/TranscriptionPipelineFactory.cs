using Rizedown.AudioCapture;
using Rizedown.Models;
#if MACCATALYST
using Rizedown.Transcription.Engines.AppleSpeech;
using Rizedown.Transcription.Engines.WhisperNet;
#endif
using Rizedown.Transcription.Engines.NoOp;
using Rizedown.Transcription.Engines.Pyannote;
using Rizedown.Transcription.Engines.WhisperCpp;
using Microsoft.Extensions.Logging;

namespace Rizedown.Transcription;

/// <summary>
/// Assembles a <see cref="ITranscriptionPipeline"/> from the engine types selected in
/// preferences, injecting the caller-supplied runtime paths into each engine.
/// </summary>
public sealed class TranscriptionPipelineFactory : ITranscriptionPipelineFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISpeakerMergeStrategy _mergeStrategy;

    public TranscriptionPipelineFactory(ILoggerFactory loggerFactory, ISpeakerMergeStrategy mergeStrategy)
    {
        _loggerFactory = loggerFactory;
        _mergeStrategy = mergeStrategy;
    }

    // Used by the Preferences UI to enumerate engine names and availability.
    // Engines are constructed with empty paths here — IsAvailable reflects
    // whether the binary/model exist once the user has configured them.
    public IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines =>
    [
#if MACCATALYST
        new AppleSpeechTranscriptionEngine(
            _loggerFactory.CreateLogger<AppleSpeechTranscriptionEngine>()),
        new WhisperNetTranscriptionEngine(
            string.Empty,
            _loggerFactory.CreateLogger<WhisperNetTranscriptionEngine>()),
#else
        new WhisperCppTranscriptionEngine(
            string.Empty, string.Empty,
            _loggerFactory.CreateLogger<WhisperCppTranscriptionEngine>())
#endif
    ];

    public IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines =>
    [
        new NoDiarizationEngine(),
#if !MACCATALYST
        new PyannoteDiarizationEngine(
            string.Empty,
            string.Empty,
            _loggerFactory.CreateLogger<PyannoteDiarizationEngine>())
#endif
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
            TranscriptionEngineType.WhisperNet =>
                new WhisperNetTranscriptionEngine(
                    whisperModelPath,
                    _loggerFactory.CreateLogger<WhisperNetTranscriptionEngine>()),
            TranscriptionEngineType.WhisperCpp =>
                throw new PlatformNotSupportedException(
                    "Whisper.cpp requires a subprocess and cannot run in the Mac App Store sandbox. " +
                    "Use Apple Speech or Whisper.net instead."),
#else
            TranscriptionEngineType.WhisperCpp =>
                new WhisperCppTranscriptionEngine(
                    whisperBinaryPath, whisperModelPath,
                    _loggerFactory.CreateLogger<WhisperCppTranscriptionEngine>()),
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };

        var diarizationEngine = diarization switch
        {
            DiarizationEngineType.None =>
                (IDiarizationEngine)new NoDiarizationEngine(),
#if MACCATALYST
            DiarizationEngineType.Pyannote =>
                throw new PlatformNotSupportedException(
                    "pyannote.audio requires a Python subprocess and cannot run in the Mac App Store sandbox. " +
                    "Speaker diarization is unavailable on this platform."),
#else
            DiarizationEngineType.Pyannote =>
                new PyannoteDiarizationEngine(
                    pyannotePythonPath,
                    pyannoteHfToken,
                    _loggerFactory.CreateLogger<PyannoteDiarizationEngine>()),
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(diarization), diarization, null)
        };

        return new StandardTranscriptionPipeline(
            transcriptionEngine,
            diarizationEngine,
            _mergeStrategy,
            _loggerFactory.CreateLogger<StandardTranscriptionPipeline>());
    }

    public ILiveTranscriptionSession? CreateLiveSession(
        TranscriptionEngineType engine,
        string whisperBinaryPath = "",
        string whisperModelPath = "",
        INativeMicrophoneSource? nativeMicSource = null)
    {
        return engine switch
        {
#if MACCATALYST
            TranscriptionEngineType.AppleSpeech =>
                new AppleSpeechTranscriptionEngine(
                    _loggerFactory.CreateLogger<AppleSpeechTranscriptionEngine>())
                .CreateLiveSession(nativeMicSource),
#else
            TranscriptionEngineType.WhisperCpp =>
                new WhisperCppTranscriptionEngine(
                    whisperBinaryPath, whisperModelPath,
                    _loggerFactory.CreateLogger<WhisperCppTranscriptionEngine>())
                .CreateLiveSession(),
#endif
            // WhisperNet live session not yet implemented; live transcription falls back to silence.
            _ => null
        };
    }
}
