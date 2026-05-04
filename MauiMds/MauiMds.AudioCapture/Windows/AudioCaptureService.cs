#pragma warning disable CA1416  // MediaFoundation/WASAPI APIs require Windows 10 build 19041+; this file is Windows-only.
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private AudioCaptureState _state = AudioCaptureState.Idle;

    // Microphone capture
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _micWaveWriter;
    private string? _tempMicWavPath;
    private string? _desiredMicOutputPath;

    // System audio (WASAPI loopback)
    private SystemAudioLoopback? _sysAudio;
    private string? _tempSysWavPath;
    private string? _desiredSysOutputPath;

    private int _encoderBitRate;
    private DateTimeOffset _recordingStarted;

    // Raw GUID for MPEG Layer 3 (MP3) — avoids NAudio AudioSubtypes member availability issues.
    private static readonly Guid Mp3SubType = new("00000055-0000-0010-8000-00aa00389b71");

    public AudioCaptureState State => _state;
    public string? LastStartWarning { get; private set; }
    public event EventHandler<AudioCaptureState>? StateChanged;

    public AudioCaptureService(ILogger<AudioCaptureService> logger)
    {
        _logger = logger;
    }

    // Unpackaged Windows apps don't have an app-level microphone permission dialog.
    // Microphone access is governed by Windows Privacy Settings at the OS level.
    // If access is blocked there, WaveInEvent.StartRecording() will throw.
    public Task<AudioPermissionStatus> CheckMicrophonePermissionAsync() =>
        Task.FromResult(AudioPermissionStatus.Granted);

    public Task<AudioPermissionStatus> RequestMicrophonePermissionAsync() =>
        Task.FromResult(AudioPermissionStatus.Granted);

    public async Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
        if (!options.CaptureMicrophone && !options.CaptureSystemAudio)
            throw new ArgumentException("At least one audio source must be enabled.", nameof(options));

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != AudioCaptureState.Idle)
                throw new InvalidOperationException($"Cannot start capture from state {_state}.");

            SetState(AudioCaptureState.Starting);
            LastStartWarning = null;
            _encoderBitRate = options.EncoderBitRate;

            var micDir = options.CaptureMicrophone
                ? Path.GetDirectoryName(options.OutputPath) ?? string.Empty
                : string.Empty;

            if (!string.IsNullOrEmpty(micDir))
                Directory.CreateDirectory(micDir);

            // Start microphone capture if requested.
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
                _waveIn.DataAvailable += OnMicDataAvailable;
                _waveIn.StartRecording();
            }

            // Start system audio (WASAPI loopback) capture if requested.
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
                    _logger.LogInformation("AudioCaptureService: WASAPI loopback started.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AudioCaptureService: WASAPI loopback unavailable; continuing mic-only.");
                    _sysAudio?.Dispose();
                    _sysAudio = null;
                    TryDelete(_tempSysWavPath);
                    _tempSysWavPath = null;
                    _desiredSysOutputPath = null;

                    if (options.CaptureMicrophone)
                        LastStartWarning = "screen_recording_denied";
                    else
                        throw;
                }
            }

            _recordingStarted = DateTimeOffset.UtcNow;
            SetState(AudioCaptureState.Recording);
        }
        catch
        {
            _waveIn?.Dispose(); _waveIn = null;
            _micWaveWriter?.Dispose(); _micWaveWriter = null;
            TryDelete(_tempMicWavPath); _tempMicWavPath = null;
            _sysAudio?.Dispose(); _sysAudio = null;
            TryDelete(_tempSysWavPath); _tempSysWavPath = null;
            _desiredMicOutputPath = null;
            _desiredSysOutputPath = null;
            SetState(AudioCaptureState.Idle);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<AudioCaptureResult> StopAsync()
    {
        string? tempMicWav, desiredMic, tempSysWav, desiredSys;
        int bitRate;
        DateTimeOffset started;

        await _stateLock.WaitAsync();
        try
        {
            if (_state != AudioCaptureState.Recording)
                return new AudioCaptureResult { Success = false, ErrorMessage = "Not recording." };

            SetState(AudioCaptureState.Stopping);

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

            // Stop system audio.
            _sysAudio?.Stop();
            _sysAudio?.Dispose();
            _sysAudio = null;

            tempMicWav = _tempMicWavPath;
            desiredMic = _desiredMicOutputPath;
            tempSysWav = _tempSysWavPath;
            desiredSys = _desiredSysOutputPath;
            bitRate = _encoderBitRate;
            started = _recordingStarted;

            _tempMicWavPath = null; _desiredMicOutputPath = null;
            _tempSysWavPath = null; _desiredSysOutputPath = null;
        }
        finally
        {
            _stateLock.Release();
        }

        // Encode both streams outside the lock — can take several seconds.
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

        SetState(AudioCaptureState.Idle);

        return new AudioCaptureResult
        {
            Success = true,
            AudioFilePaths = outputPaths,
            Duration = duration,
            ErrorMessage = warning,
        };
    }

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
                        TryDelete(tempWavPath);
                        _logger.LogInformation("AudioCaptureService: MP3 encoded via MediaFoundation.");
                        return (desiredOutputPath, null);
                    }
                    EncodeToMp3WithLame(tempWavPath, desiredOutputPath, bitRate);
                    TryDelete(tempWavPath);
                    _logger.LogInformation("AudioCaptureService: MP3 encoded via NAudio.Lame.");
                    return (desiredOutputPath, null);

                case ".flac":
                    if (await TryEncodeToFlacWithFfmpegAsync(tempWavPath, desiredOutputPath))
                    {
                        TryDelete(tempWavPath);
                        _logger.LogInformation("AudioCaptureService: FLAC encoded via ffmpeg.");
                        return (desiredOutputPath, null);
                    }
                    var wavFallback = Path.ChangeExtension(desiredOutputPath, ".wav");
                    File.Move(tempWavPath, wavFallback, overwrite: true);
                    _logger.LogWarning("AudioCaptureService: ffmpeg unavailable; saved as WAV instead of FLAC.");
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
            _logger.LogError(ex, "AudioCaptureService: encoding failed; returning temp WAV.");
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
            _logger.LogWarning(ex, "AudioCaptureService: MediaFoundation MP3 encoding failed; falling back to LAME.");
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
            _logger.LogWarning(ex, "AudioCaptureService: ffmpeg FLAC encoding failed.");
            return false;
        }
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
        => _micWaveWriter?.Write(e.Buffer, 0, e.BytesRecorded);

    private void SetState(AudioCaptureState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private static void TryDelete(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        _micWaveWriter?.Dispose();
        _waveIn?.Dispose();
        _sysAudio?.Dispose();
        TryDelete(_tempMicWavPath);
        TryDelete(_tempSysWavPath);
    }
}
