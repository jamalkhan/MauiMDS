using AVFoundation;
using AudioToolbox;
using CoreMedia;
using Foundation;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

/// <summary>
/// Writes mic and/or system audio to a single M4A file.
///
/// When both sources are active, each gets its own AVAssetWriter keyed to its own
/// clock domain (SCK and AVCaptureSession use different clocks). On finish the two
/// temp files are merged into one via AVMutableComposition so that both tracks
/// start at T=0 and play to their full individual durations.
/// </summary>
internal sealed class AudioFileWriter : IDisposable
{
    private readonly ILogger _logger;
    private readonly AudioCaptureOptions _options;

    // -- mic writer
    private readonly AVAssetWriter? _micWriter;
    private readonly AVAssetWriterInput? _micInput;
    private readonly string? _micTempPath;
    private bool _micSessionStarted;

    // -- system audio writer
    private readonly AVAssetWriter? _sysWriter;
    private readonly AVAssetWriterInput? _sysInput;
    private readonly string? _sysTempPath;
    private bool _sysSessionStarted;

    private readonly object _lock = new();
    private bool _finished;
    private bool _disposed;
    private DateTimeOffset _startedAt;

    public AudioFileWriter(AudioCaptureOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _startedAt = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(options.OutputPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(options.OutputPath);

        if (options.CaptureMicrophone)
        {
            // When both sources are active, write mic to a temp file and merge later.
            // When mic-only, write directly to the final path.
            _micTempPath = options.CaptureSystemAudio
                ? Path.Combine(dir, $"{stem}.tmp_mic.m4a")
                : options.OutputPath;

            _micWriter = CreateWriter(_micTempPath);
            _micInput = CreateWriterInput(options);
            _micWriter.AddInput(_micInput);
            StartWriter(_micWriter);
        }

        if (options.CaptureSystemAudio)
        {
            _sysTempPath = options.CaptureMicrophone
                ? Path.Combine(dir, $"{stem}.tmp_sys.m4a")
                : options.OutputPath;

            _sysWriter = CreateWriter(_sysTempPath);
            _sysInput = CreateWriterInput(options);
            _sysWriter.AddInput(_sysInput);
            StartWriter(_sysWriter);
        }

        _logger.LogInformation("AudioFileWriter: writer ready → {Path}", options.OutputPath);
    }

    public void AppendMicAudio(CMSampleBuffer buffer) =>
        AppendBuffer(buffer, _micWriter, _micInput, ref _micSessionStarted, "mic");

    public void AppendSystemAudio(CMSampleBuffer buffer) =>
        AppendBuffer(buffer, _sysWriter, _sysInput, ref _sysSessionStarted, "sys");

    private void AppendBuffer(
        CMSampleBuffer buffer,
        AVAssetWriter? writer,
        AVAssetWriterInput? input,
        ref bool sessionStarted,
        string label)
    {
        if (writer is null || input is null || _finished) return;

        lock (_lock)
        {
            if (!sessionStarted)
            {
                writer.StartSessionAtSourceTime(buffer.PresentationTimeStamp);
                sessionStarted = true;
            }
        }

        if (input.ReadyForMoreMediaData && !input.AppendSampleBuffer(buffer))
        {
            _logger.LogWarning("AudioFileWriter: failed to append {Label} buffer — {Error}",
                label, writer.Error?.LocalizedDescription);
        }
    }

    public async Task<AudioCaptureResult> FinishAsync()
    {
        if (_finished) return Failure("Already finished.");
        _finished = true;

        var micHasData = _micSessionStarted;
        var sysHasData = _sysSessionStarted;

        if (!micHasData && !sysHasData)
        {
            _micWriter?.CancelWriting();
            _sysWriter?.CancelWriting();
            return Failure("No audio data was captured before stop was called.");
        }

        // Finish whichever writers were used.
        if (micHasData)
        {
            _micInput!.MarkAsFinished();
            await _micWriter!.FinishWritingAsync();
            if (_micWriter.Status == AVAssetWriterStatus.Failed)
                return Failure(_micWriter.Error?.LocalizedDescription ?? "Mic writer failed.");
        }
        else
        {
            _micWriter?.CancelWriting();
        }

        if (sysHasData)
        {
            _sysInput!.MarkAsFinished();
            await _sysWriter!.FinishWritingAsync();
            if (_sysWriter.Status == AVAssetWriterStatus.Failed)
                return Failure(_sysWriter.Error?.LocalizedDescription ?? "System audio writer failed.");
        }
        else
        {
            _sysWriter?.CancelWriting();
        }

        // If only one source is active, it already wrote to the final path — done.
        if (!(micHasData && sysHasData))
        {
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

        // Both sources active — merge temp files into the final output.
        return await MergeAsync();
    }

    private async Task<AudioCaptureResult> MergeAsync()
    {
        _logger.LogInformation("AudioFileWriter: merging mic + system audio tracks.");
        try
        {
            var micAsset = AVUrlAsset.FromUrl(NSUrl.FromFilename(_micTempPath!));
            var sysAsset = AVUrlAsset.FromUrl(NSUrl.FromFilename(_sysTempPath!));

            var micAudioTracks = micAsset.GetTracks(AVMediaTypes.Audio);
            var sysAudioTracks = sysAsset.GetTracks(AVMediaTypes.Audio);

            var composition = AVMutableComposition.Create();

            if (micAudioTracks.Length > 0)
            {
                var srcTrack = micAudioTracks[0];
                var compTrack = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0)!;
                compTrack.InsertTimeRange(
                    new CMTimeRange { Start = CMTime.Zero, Duration = micAsset.Duration },
                    srcTrack, CMTime.Zero, out _);
            }

            if (sysAudioTracks.Length > 0)
            {
                var srcTrack = sysAudioTracks[0];
                var compTrack = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0)!;
                compTrack.InsertTimeRange(
                    new CMTimeRange { Start = CMTime.Zero, Duration = sysAsset.Duration },
                    srcTrack, CMTime.Zero, out _);
            }

            // Delete any pre-existing file at the output path (AVAssetExportSession won't overwrite).
            if (File.Exists(_options.OutputPath))
                File.Delete(_options.OutputPath);

            var export = AVAssetExportSession.FromAsset(composition, AVAssetExportSession.PresetAppleM4A)
                ?? throw new InvalidOperationException("Could not create AVAssetExportSession.");

            export.OutputUrl = NSUrl.FromFilename(_options.OutputPath);
            export.OutputFileType = "com.apple.m4a-audio";

            await export.ExportTaskAsync();

            if (export.Status == AVAssetExportSessionStatus.Failed)
                return Failure(export.Error?.LocalizedDescription ?? "Export failed.");

            var duration = DateTimeOffset.UtcNow - _startedAt;
            _logger.LogInformation("AudioFileWriter: finished (merged). Duration={Duration:g}, Path={Path}",
                duration, _options.OutputPath);
            return new AudioCaptureResult
            {
                Success = true,
                FilePath = _options.OutputPath,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioFileWriter: merge failed, keeping mic-only recording.");
            try
            {
                if (File.Exists(_micTempPath) && !File.Exists(_options.OutputPath))
                    File.Move(_micTempPath!, _options.OutputPath);
            }
            catch { /* best-effort */ }
            return Failure($"Merge failed: {ex.Message}");
        }
        finally
        {
            TryDelete(_micTempPath);
            TryDelete(_sysTempPath);
        }
    }

    private static AVAssetWriter CreateWriter(string path)
    {
        var writer = new AVAssetWriter(
            NSUrl.FromFilename(path),
            "com.apple.m4a-audio",
            out NSError? err);
        if (err is not null)
            throw new InvalidOperationException($"Could not create AVAssetWriter: {err.LocalizedDescription}");
        return writer;
    }

    private static void StartWriter(AVAssetWriter writer)
    {
        if (!writer.StartWriting())
            throw new InvalidOperationException(
                $"AVAssetWriter failed to start: {writer.Error?.LocalizedDescription}");
    }

    private static AVAssetWriterInput CreateWriterInput(AudioCaptureOptions options)
    {
        var settings = new AudioSettings
        {
            Format = AudioFormatType.MPEG4AAC,
            SampleRate = options.SampleRate,
            NumberChannels = options.ChannelCount,
            EncoderBitRate = options.EncoderBitRate
        };
        return new AVAssetWriterInput("soun", settings) { ExpectsMediaDataInRealTime = true };
    }

    private static void TryDelete(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { /* best-effort */ }
    }

    private static AudioCaptureResult Failure(string message) =>
        new() { Success = false, ErrorMessage = message };

    public void Dispose()
    {
        if (_disposed) return;
        if (!_finished)
        {
            _micWriter?.CancelWriting();
            _sysWriter?.CancelWriting();
        }
        _micInput?.Dispose();
        _micWriter?.Dispose();
        _sysInput?.Dispose();
        _sysWriter?.Dispose();
        _disposed = true;
    }
}
