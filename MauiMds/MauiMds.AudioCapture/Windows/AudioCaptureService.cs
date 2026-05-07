#pragma warning disable CA1416  // MediaFoundation/WASAPI APIs require Windows 10 build 19041+; this file is Windows-only.
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

public sealed class AudioCaptureService : AudioCaptureServiceBase
{
    // Microphone capture
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _micWaveWriter;
    private string? _tempMicWavPath;
    private string? _desiredMicOutputPath;

    // System audio (WASAPI loopback)
    private SystemAudioLoopback? _sysAudio;
    private string? _tempSysWavPath;
    private string? _desiredSysOutputPath;

    // Live chunk emission
    private WindowsLiveChunkWriter? _liveChunkWriter;

    private int _encoderBitRate;
    private DateTimeOffset _recordingStarted;

    // Raw GUID for MPEG Layer 3 (MP3) — avoids NAudio AudioSubtypes member availability issues.
    private static readonly Guid Mp3SubType = new("00000055-0000-0010-8000-00aa00389b71");

    public AudioCaptureService(ILogger<AudioCaptureService> logger) : base(logger) { }

    // ── Permissions (Windows Privacy Settings govern mic access at OS level) ──

    // Unpackaged Windows apps don't have an app-level microphone permission dialog.
    // If access is blocked, WaveInEvent.StartRecording() will throw.
    public override Task<AudioPermissionStatus> CheckMicrophonePermissionAsync() =>
        Task.FromResult(AudioPermissionStatus.Granted);

    public override Task<AudioPermissionStatus> RequestMicrophonePermissionAsync() =>
        Task.FromResult(AudioPermissionStatus.Granted);

    // ── Platform hooks ────────────────────────────────────────────────────────

    protected override async Task StartCaptureAsync(AudioCaptureOptions options, CancellationToken cancellationToken)
    {
        _encoderBitRate = options.EncoderBitRate;

        var micDir = options.CaptureMicrophone
            ? Path.GetDirectoryName(options.OutputPath) ?? string.Empty
            : string.Empty;

        if (!string.IsNullOrEmpty(micDir))
            Directory.CreateDirectory(micDir);

        if (options.CaptureMicrophone)
        {
            _desiredMicOutputPath = options.OutputPath;
            _tempMicWavPath = Path.Combine(micDir,
                Path.GetFileNameWithoutExtension(options.OutputPath) + ".tmp.wav");

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(options.SampleRate, 16, options.ChannelCount),
                BufferMilliseconds = 50,
            };
            _micWaveWriter = new WaveFileWriter(_tempMicWavPath, _waveIn.WaveFormat);

            if (options.EnableLiveChunks)
            {
                _liveChunkWriter = new WindowsLiveChunkWriter(_waveIn.WaveFormat, options.LiveChunkInterval, Logger);
                _liveChunkWriter.ChunkReady += (_, chunk) => RaiseLiveChunkAvailable(chunk);
            }

            _waveIn.DataAvailable += OnMicDataAvailable;
            _waveIn.StartRecording();
        }

        if (options.CaptureSystemAudio)
        {
            try
            {
                var sysDir = Path.GetDirectoryName(options.SysOutputPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(sysDir))
                    Directory.CreateDirectory(sysDir);

                _desiredSysOutputPath = options.SysOutputPath;
                _tempSysWavPath = Path.Combine(sysDir,
                    Path.GetFileNameWithoutExtension(options.SysOutputPath) + ".tmp.wav");

                _sysAudio = new SystemAudioLoopback();
                _sysAudio.Start(_tempSysWavPath);
                Logger.LogInformation("AudioCaptureService: WASAPI loopback started.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "AudioCaptureService: WASAPI loopback unavailable; continuing mic-only.");
                _sysAudio?.Dispose();
                _sysAudio = null;
                TryDeleteFile(_tempSysWavPath);
                _tempSysWavPath = null;
                _desiredSysOutputPath = null;

                if (options.CaptureMicrophone)
                    LastStartWarning = AudioCaptureWarnings.WasapiLoopbackUnavailable;
                else
                    throw;
            }
        }

        _recordingStarted = DateTimeOffset.UtcNow;
        await Task.CompletedTask; // StartCaptureAsync is synchronous on Windows
    }

    protected override Task CleanupAfterFailedStartAsync()
    {
        _waveIn?.Dispose(); _waveIn = null;
        _micWaveWriter?.Dispose(); _micWaveWriter = null;
        TryDeleteFile(_tempMicWavPath); _tempMicWavPath = null;
        _sysAudio?.Dispose(); _sysAudio = null;
        TryDeleteFile(_tempSysWavPath); _tempSysWavPath = null;
        _liveChunkWriter?.Dispose(); _liveChunkWriter = null;
        _desiredMicOutputPath = null;
        _desiredSysOutputPath = null;
        return Task.CompletedTask;
    }

    protected override async Task<AudioCaptureResult> StopCaptureAsync()
    {
        // Stop microphone.
        if (_waveIn is not null)
        {
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= OnMicDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;

            _micWaveWriter!.Flush();
            _micWaveWriter.Dispose();
            _micWaveWriter = null;
        }

        // Flush the last live chunk (if any) before signalling that recording stopped.
        if (_liveChunkWriter is not null)
        {
            await _liveChunkWriter.FlushLastChunkAsync();
            _liveChunkWriter.Dispose();
            _liveChunkWriter = null;
        }

        // Stop system audio.
        _sysAudio?.Stop();
        _sysAudio?.Dispose();
        _sysAudio = null;

        // Capture and clear per-recording state before the async encode step.
        var tempMicWav = _tempMicWavPath;
        var desiredMic = _desiredMicOutputPath;
        var tempSysWav = _tempSysWavPath;
        var desiredSys = _desiredSysOutputPath;
        var bitRate = _encoderBitRate;
        var started = _recordingStarted;

        _tempMicWavPath = null; _desiredMicOutputPath = null;
        _tempSysWavPath = null; _desiredSysOutputPath = null;

        // Encode — can take several seconds.
        var duration = DateTimeOffset.UtcNow - started;
        var outputPaths = new List<string>(2);
        string? warning = null;

        if (tempMicWav is not null && desiredMic is not null)
        {
            var (path, w) = await EncodeAsync(tempMicWav, desiredMic, bitRate);
            outputPaths.Add(path);
            warning ??= w;
        }

        if (tempSysWav is not null && desiredSys is not null)
        {
            var (path, w) = await EncodeAsync(tempSysWav, desiredSys, bitRate);
            outputPaths.Add(path);
            warning ??= w;
        }

        return new AudioCaptureResult
        {
            Success = true,
            AudioFilePaths = outputPaths,
            Duration = duration,
            ErrorMessage = warning,
        };
    }

    protected override void DisposeResources()
    {
        _micWaveWriter?.Dispose();
        _waveIn?.Dispose();
        _sysAudio?.Dispose();
        _liveChunkWriter?.Dispose();
        TryDeleteFile(_tempMicWavPath);
        TryDeleteFile(_tempSysWavPath);
    }

    // ── Windows-specific encoding ─────────────────────────────────────────────

    private async Task<(string outputPath, string? warning)> EncodeAsync(
        string tempWavPath, string desiredOutputPath, int bitRate)
    {
        var ext = Path.GetExtension(desiredOutputPath).ToLowerInvariant();
        try
        {
            switch (ext)
            {
                case ".mp3":
                    if (TryEncodeToMp3WithMediaFoundation(tempWavPath, desiredOutputPath, bitRate))
                    {
                        TryDeleteFile(tempWavPath);
                        Logger.LogInformation("AudioCaptureService: MP3 encoded via MediaFoundation.");
                        return (desiredOutputPath, null);
                    }
                    EncodeToMp3WithLame(tempWavPath, desiredOutputPath, bitRate);
                    TryDeleteFile(tempWavPath);
                    Logger.LogInformation("AudioCaptureService: MP3 encoded via NAudio.Lame.");
                    return (desiredOutputPath, null);

                case ".flac":
                    if (await TryEncodeToFlacWithFfmpegAsync(tempWavPath, desiredOutputPath))
                    {
                        TryDeleteFile(tempWavPath);
                        Logger.LogInformation("AudioCaptureService: FLAC encoded via ffmpeg.");
                        return (desiredOutputPath, null);
                    }
                    var wavFallback = Path.ChangeExtension(desiredOutputPath, ".wav");
                    File.Move(tempWavPath, wavFallback, overwrite: true);
                    Logger.LogWarning("AudioCaptureService: ffmpeg unavailable; saved as WAV instead of FLAC.");
                    return (wavFallback, "ffmpeg not found in PATH; recording saved as WAV.");

                default:
                    // .wav or .m4a (m4a not supported on Windows — save as .wav)
                    var wavOut = Path.ChangeExtension(desiredOutputPath, ".wav");
                    File.Move(tempWavPath, wavOut, overwrite: true);
                    return (wavOut, null);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "AudioCaptureService: encoding failed; returning temp WAV.");
            return (tempWavPath, $"Encoding failed: {ex.Message}");
        }
    }

    private bool TryEncodeToMp3WithMediaFoundation(string wavPath, string mp3Path, int bitRate)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            var mediaType = MediaFoundationEncoder.SelectMediaType(Mp3SubType, reader.WaveFormat, bitRate);
            if (mediaType is null)
                return false;

            using var encoder = new MediaFoundationEncoder(mediaType);
            encoder.Encode(mp3Path, reader);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AudioCaptureService: MediaFoundation MP3 encoding failed; falling back to LAME.");
            return false;
        }
    }

    private static void EncodeToMp3WithLame(string wavPath, string mp3Path, int bitRate)
    {
        using var reader = new WaveFileReader(wavPath);
        using var writer = new NAudio.Lame.LameMP3FileWriter(mp3Path, reader.WaveFormat, bitRate / 1000);
        reader.CopyTo(writer);
    }

    private async Task<bool> TryEncodeToFlacWithFfmpegAsync(string wavPath, string flacPath)
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg")
            {
                Arguments = $"-y -i \"{wavPath}\" -compression_level 8 \"{flacPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && File.Exists(flacPath);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AudioCaptureService: ffmpeg FLAC encoding failed.");
            return false;
        }
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        _micWaveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        _liveChunkWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }
}
