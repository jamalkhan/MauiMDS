using AVFoundation;
using AudioToolbox;
using CoreMedia;
using Foundation;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

/// <summary>
/// Writes a single audio source (mic OR system audio) to an M4A file via AVAssetWriter.
/// One instance per source — the caller creates two instances for dual-source recordings.
/// </summary>
internal sealed class AudioFileWriter : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _outputPath;
    private readonly string _label;

    private readonly AVAssetWriter _writer;
    private readonly AVAssetWriterInput _input;

    private readonly object _lock = new();
    private bool _sessionStarted;
    private bool _finished;
    private bool _disposed;
    private readonly DateTimeOffset _startedAt;

    public AudioFileWriter(string outputPath, AudioCaptureOptions options, string label, ILogger logger)
    {
        _outputPath = outputPath;
        _label = label;
        _logger = logger;
        _startedAt = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _writer = new AVAssetWriter(NSUrl.FromFilename(outputPath), "com.apple.m4a-audio", out NSError? err);
        if (err is not null)
            throw new InvalidOperationException($"Could not create AVAssetWriter for {label}: {err.LocalizedDescription}");

        var settings = new AudioSettings
        {
            Format = AudioFormatType.MPEG4AAC,
            SampleRate = options.SampleRate,
            NumberChannels = options.ChannelCount,
            EncoderBitRate = options.EncoderBitRate
        };
        _input = new AVAssetWriterInput("soun", settings) { ExpectsMediaDataInRealTime = true };
        _writer.AddInput(_input);

        if (!_writer.StartWriting())
            throw new InvalidOperationException(
                $"AVAssetWriter failed to start for {label}: {_writer.Error?.LocalizedDescription}");

        _logger.LogInformation("AudioFileWriter ({Label}): ready → {Path}", label, outputPath);
    }

    public bool HasData => _sessionStarted;

    public void AppendBuffer(CMSampleBuffer buffer)
    {
        if (_finished) return;

        lock (_lock)
        {
            if (!_sessionStarted)
            {
                _writer.StartSessionAtSourceTime(buffer.PresentationTimeStamp);
                _sessionStarted = true;
            }
        }

        if (_input.ReadyForMoreMediaData && !_input.AppendSampleBuffer(buffer))
        {
            _logger.LogWarning("AudioFileWriter ({Label}): failed to append buffer — {Error}",
                _label, _writer.Error?.LocalizedDescription);
        }
    }

    public async Task<AudioCaptureResult> FinishAsync()
    {
        if (_finished) return Failure("Already finished.");
        _finished = true;

        if (!_sessionStarted)
        {
            _writer.CancelWriting();
            return Failure($"No audio data captured for {_label} before stop was called.");
        }

        _input.MarkAsFinished();
        await _writer.FinishWritingAsync();

        if (_writer.Status == AVAssetWriterStatus.Failed)
            return Failure(_writer.Error?.LocalizedDescription ?? $"{_label} writer failed.");

        var duration = DateTimeOffset.UtcNow - _startedAt;
        _logger.LogInformation("AudioFileWriter ({Label}): finished. Duration={Duration:g}, Path={Path}",
            _label, duration, _outputPath);

        return new AudioCaptureResult
        {
            Success = true,
            AudioFilePaths = [_outputPath],
            Duration = duration
        };
    }

    private static AudioCaptureResult Failure(string message) =>
        new() { Success = false, ErrorMessage = message };

    public void Dispose()
    {
        if (_disposed) return;
        if (!_finished)
            _writer.CancelWriting();
        _input.Dispose();
        _writer.Dispose();
        _disposed = true;
    }
}
