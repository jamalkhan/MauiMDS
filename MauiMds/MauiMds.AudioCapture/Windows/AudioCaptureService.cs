using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using NAudio.Wave;

namespace MauiMds.AudioCapture.Windows;

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private AudioCaptureState _state = AudioCaptureState.Idle;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _outputPath;
    private DateTimeOffset _recordingStarted;

    public AudioCaptureState State => _state;
    public string? LastStartWarning { get; private set; }
    public event EventHandler<AudioCaptureState>? StateChanged;

    public AudioCaptureService(ILogger<AudioCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task<AudioPermissionStatus> CheckMicrophonePermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        return MapStatus(status);
    }

    public async Task<AudioPermissionStatus> RequestMicrophonePermissionAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        return MapStatus(status);
    }

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

            // NAudio records PCM WAV natively — store as .wav regardless of the requested extension.
            _outputPath = Path.ChangeExtension(options.OutputPath, ".wav");
            var dir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(options.SampleRate, 16, options.ChannelCount),
                BufferMilliseconds = 50,
            };
            _waveWriter = new WaveFileWriter(_outputPath, _waveIn.WaveFormat);
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

            var path = _outputPath!;
            _outputPath = null;
            var duration = DateTimeOffset.UtcNow - _recordingStarted;

            SetState(AudioCaptureState.Idle);

            return new AudioCaptureResult
            {
                Success = true,
                AudioFilePaths = [path],
                Duration = duration,
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);

    private void SetState(AudioCaptureState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private static AudioPermissionStatus MapStatus(PermissionStatus status) => status switch
    {
        PermissionStatus.Granted => AudioPermissionStatus.Granted,
        PermissionStatus.Denied or PermissionStatus.Restricted => AudioPermissionStatus.Denied,
        _ => AudioPermissionStatus.NotDetermined,
    };

    public void Dispose()
    {
        _waveWriter?.Dispose();
        _waveIn?.Dispose();
    }
}
