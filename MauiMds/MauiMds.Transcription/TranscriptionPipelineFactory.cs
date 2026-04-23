using MauiMds.Models;
using MauiMds.Transcription.Engines.AppleSpeech;
using MauiMds.Transcription.Engines.NoOp;
using MauiMds.Transcription.Engines.Pyannote;
using MauiMds.Transcription.Engines.WhisperCpp;
using Microsoft.Extensions.Logging;

namespace MauiMds.Transcription;

/// <summary>
/// Abstract factory: assembles a <see cref="ITranscriptionPipeline"/> from
/// the engine types selected in preferences. Add new engine registrations here
/// as they are implemented.
/// </summary>
public sealed class TranscriptionPipelineFactory : ITranscriptionPipelineFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public TranscriptionPipelineFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyList<ITranscriptionEngine> AvailableTranscriptionEngines =>
    [
        new AppleSpeechTranscriptionEngine(
            _loggerFactory.CreateLogger<AppleSpeechTranscriptionEngine>()),
        new WhisperCppTranscriptionEngine(string.Empty)
    ];

    public IReadOnlyList<IDiarizationEngine> AvailableDiarizationEngines =>
    [
        new NoDiarizationEngine(),
        new PyannoteDiarizationEngine(string.Empty)
    ];

    public ITranscriptionPipeline Create(
        TranscriptionEngineType engine,
        DiarizationEngineType diarization)
    {
        var transcriptionEngine = engine switch
        {
            TranscriptionEngineType.AppleSpeech =>
                (ITranscriptionEngine)new AppleSpeechTranscriptionEngine(
                    _loggerFactory.CreateLogger<AppleSpeechTranscriptionEngine>()),
            TranscriptionEngineType.WhisperCpp =>
                new WhisperCppTranscriptionEngine(string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };

        var diarizationEngine = diarization switch
        {
            DiarizationEngineType.None =>
                (IDiarizationEngine)new NoDiarizationEngine(),
            DiarizationEngineType.Pyannote =>
                new PyannoteDiarizationEngine(string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(diarization), diarization, null)
        };

        return new StandardTranscriptionPipeline(
            transcriptionEngine,
            diarizationEngine,
            _loggerFactory.CreateLogger<StandardTranscriptionPipeline>());
    }
}
