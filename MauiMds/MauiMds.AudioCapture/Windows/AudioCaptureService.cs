using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private AudioCaptureState _state = AudioCaptureState.Idle;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _tempWavPath;   // intermediate PCM file written during capture
    private string? _mp3OutputPath; // final .mp3 path returned to the caller
    private int _encoderBitRate;
    private DateTimeOffset _recordingStarted;

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

            // System audio capture (WASAPI loopback) is not yet implemented on Windows.
            if (options.CaptureSystemAudio)
            {
                _logger.LogWarning("System audio capture is not yet supported on Windows; recording microphone only.");
                LastStartWarning = "screen_recording_denied";
            }

            if (!options.CaptureMicrophone)
                throw new InvalidOperationException("Microphone capture must be enabled; system audio is not yet supported on Windows.");

            // Capture to a temp PCM WAV; encode to MP3 after stop via Media Foundation.
            _mp3OutputPath = Path.ChangeExtension(options.OutputPath, ".mp3");
            _tempWavPath   = Path.ChangeExtension(options.OutputPath, ".tmp.wav");
            _encoderBitRate = options.EncoderBitRate;

            var dir = Path.GetDirectoryName(_mp3OutputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(options.SampleRate, 16, options.ChannelCount),
                BufferMilliseconds = 50,
            };
            _waveWriter = new WaveFileWriter(_tempWavPath, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();

            _recordingStarted = DateTimeOffset.UtcNow;
            SetState(AudioCaptureState.Recording);
        }
        catch
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _waveWriter?.Dispose();
            _waveWriter = null;
            TryDelete(_tempWavPath);
            _tempWavPath = null;
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
        await _stateLock.WaitAsync();
        try
        {
            if (_state != AudioCaptureState.Recording)
                return new AudioCaptureResult { Success = false, ErrorMessage = "Not recording." };

            SetState(AudioCaptureState.Stopping);

            _waveIn!.StopRecording();
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;

            _waveWriter!.Flush();
            _waveWriter.Dispose();
            _waveWriter = null;

            var tempWav = _tempWavPath!;
            var mp3Path = _mp3OutputPath!;
            _tempWavPath = null;
            _mp3OutputPath = null;
            var duration = DateTimeOffset.UtcNow - _recordingStarted;

            // Encode temp WAV → MP3 using Windows Media Foundation (no extra DLLs needed).
            _logger.LogInformation("Encoding {Wav} → {Mp3}", tempWav, mp3Path);
            EncodeToMp3(tempWav, mp3Path, _encoderBitRate);
            TryDelete(tempWav);

            SetState(AudioCaptureState.Idle);

            return new AudioCaptureResult
            {
                Success = true,
                AudioFilePaths = [mp3Path],
                Duration = duration,
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private static void EncodeToMp3(string wavPath, string mp3Path, int bitRate)
    {
        MediaFoundationApi.Startup();
        try
        {
            using var reader = new WaveFileReader(wavPath);
            var mp3Type = MediaFoundationEncoder.SelectMediaType(
                AudioSubtypes.MpegLayer3,
                reader.WaveFormat,
                bitRate);
            using var encoder = new MediaFoundationEncoder(mp3Type);
            encoder.Encode(mp3Path, reader);
        }
        finally
        {
            MediaFoundationApi.Shutdown();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);

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
        _waveWriter?.Dispose();
        _waveIn?.Dispose();
        TryDelete(_tempWavPath);
    }
}
