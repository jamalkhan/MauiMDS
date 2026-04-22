using AVFoundation;
using AudioToolbox;
using CoreMedia;
using Foundation;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

/// <summary>
/// Writes one or two audio streams (system audio + microphone) to a single M4A file.
/// Each source gets its own track inside the container.
/// </summary>
internal sealed class AudioFileWriter : IDisposable
{
    private readonly ILogger _logger;
    private readonly AudioCaptureOptions _options;
    private readonly AVAssetWriter _writer;
    private readonly AVAssetWriterInput? _systemInput;
    private readonly AVAssetWriterInput? _micInput;
    private readonly object _lock = new();
    private bool _sessionStarted;
    private bool _finished;
    private bool _disposed;

    private DateTimeOffset _startedAt;

    public AudioFileWriter(AudioCaptureOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;

        // "com.apple.m4a-audio" is the UTI for M4A audio-only container.
        _writer = new AVAssetWriter(
            NSUrl.FromFilename(options.OutputPath),
            "com.apple.m4a-audio",
            out NSError? writerError);

        if (writerError is not null)
        {
            throw new InvalidOperationException(
                $"Could not create AVAssetWriter: {writerError.LocalizedDescription}");
        }

        if (options.CaptureSystemAudio)
        {
            _systemInput = CreateWriterInput(options);
            _writer.AddInput(_systemInput);
        }

        if (options.CaptureMicrophone)
        {
            _micInput = CreateWriterInput(options);
            _writer.AddInput(_micInput);
        }

        if (!_writer.StartWriting())
        {
            throw new InvalidOperationException(
                $"AVAssetWriter failed to start writing: {_writer.Error?.LocalizedDescription}");
        }

        _startedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("AudioFileWriter: writer ready → {Path}", options.OutputPath);
    }

    public void AppendSystemAudio(CMSampleBuffer buffer)
    {
        AppendBuffer(buffer, _systemInput, "system");
    }

    public void AppendMicAudio(CMSampleBuffer buffer)
    {
        AppendBuffer(buffer, _micInput, "mic");
    }

    private void AppendBuffer(CMSampleBuffer buffer, AVAssetWriterInput? input, string label)
    {
        if (input is null || _finished)
        {
            return;
        }

        lock (_lock)
        {
            if (!_sessionStarted)
            {
                _writer.StartSessionAtSourceTime(buffer.PresentationTimeStamp);
                _sessionStarted = true;
                _logger.LogDebug("AudioFileWriter: session started at {Pts}", buffer.PresentationTimeStamp);
            }
        }

        if (input.ReadyForMoreMediaData)
        {
            if (!input.AppendSampleBuffer(buffer))
            {
                _logger.LogWarning("AudioFileWriter: failed to append {Label} buffer — {Error}",
                    label, _writer.Error?.LocalizedDescription);
            }
        }
    }

    public async Task<AudioCaptureResult> FinishAsync()
    {
        if (_finished)
        {
            return Failure("Already finished.");
        }

        _finished = true;

        if (!_sessionStarted)
        {
            _writer.CancelWriting();
            return Failure("No audio data was captured before stop was called.");
        }

        _systemInput?.MarkAsFinished();
        _micInput?.MarkAsFinished();

        await _writer.FinishWritingAsync();

        if (_writer.Status == AVAssetWriterStatus.Failed)
        {
            return Failure(_writer.Error?.LocalizedDescription ?? "Unknown AVAssetWriter error.");
        }

        var duration = DateTimeOffset.UtcNow - _startedAt;
        _logger.LogInformation("AudioFileWriter: finished. Duration={Duration:g}, Path={Path}",
            duration, _options.OutputPath);

        return new AudioCaptureResult
        {
            Success = true,
            FilePath = _options.OutputPath,
            Duration = duration
        };
    }

    private static AVAssetWriterInput CreateWriterInput(AudioCaptureOptions options)
    {
        // The writer input accepts LPCM buffers from SCStream/AVCaptureSession
        // and encodes them to AAC inside the M4A container.
        var settings = new AudioSettings
        {
            Format = AudioFormatType.MPEG4AAC,
            SampleRate = options.SampleRate,
            NumberChannels = options.ChannelCount,
            EncoderBitRate = options.EncoderBitRate
        };

        // "soun" is the UTI for audio media type.
        return new AVAssetWriterInput("soun", settings)
        {
            ExpectsMediaDataInRealTime = true
        };
    }

    private static AudioCaptureResult Failure(string message) =>
        new() { Success = false, ErrorMessage = message };

    public void Dispose()
    {
        if (!_disposed)
        {
            if (!_finished)
            {
                _writer.CancelWriting();
            }

            _systemInput?.Dispose();
            _micInput?.Dispose();
            _writer.Dispose();
            _disposed = true;
        }
    }
}
