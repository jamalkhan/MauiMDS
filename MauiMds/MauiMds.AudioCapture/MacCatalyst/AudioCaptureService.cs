using AVFoundation;
using Microsoft.Extensions.Logging;

namespace MauiMds.AudioCapture.MacCatalyst;

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private AudioCaptureState _state = AudioCaptureState.Idle;
    private SystemAudioOutput? _systemAudio;
    private MicrophoneInput? _micAudio;
    private AudioFileWriter? _writer;
    private AudioCaptureOptions? _activeOptions;

    public AudioCaptureState State => _state;
    public string? LastStartWarning { get; private set; }
    public event EventHandler<AudioCaptureState>? StateChanged;

    public AudioCaptureService(ILogger<AudioCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task<AudioPermissionStatus> CheckMicrophonePermissionAsync()
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Audio);
        return status switch
        {
            AVAuthorizationStatus.Authorized => AudioPermissionStatus.Granted,
            AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted => AudioPermissionStatus.Denied,
            _ => AudioPermissionStatus.NotDetermined
        };
    }

    public async Task<AudioPermissionStatus> RequestMicrophonePermissionAsync()
    {
        var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Audio);
        return granted ? AudioPermissionStatus.Granted : AudioPermissionStatus.Denied;
    }

    public async Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);

        if (!options.CaptureSystemAudio && !options.CaptureMicrophone)
        {
            throw new ArgumentException("At least one audio source must be enabled.", nameof(options));
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != AudioCaptureState.Idle)
            {
                throw new InvalidOperationException($"Cannot start capture from state {_state}.");
            }

            SetState(AudioCaptureState.Starting);
            LastStartWarning = null;
            _activeOptions = options;

            // Ensure output directory exists.
            var dir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            // Start sources first so we know which are actually active before
            // creating the writer (avoids ghost tracks for failed sources).
            var systemAudioActive = false;
            if (options.CaptureSystemAudio)
            {
                _systemAudio = new SystemAudioOutput(_logger);
                try
                {
                    await _systemAudio.StartAsync(options.SampleRate, options.ChannelCount);
                    systemAudioActive = true;
                }
                catch (InvalidOperationException ex) when (options.CaptureMicrophone)
                {
                    _logger.LogWarning(ex, "AudioCaptureService: system audio unavailable, falling back to microphone-only.");
                    _systemAudio.Dispose();
                    _systemAudio = null;
                    LastStartWarning = "System audio could not be captured (Screen Recording permission may need an app restart). " +
                                       "Recording microphone only.";
                }
            }

            var micActive = false;
            if (options.CaptureMicrophone)
            {
                _micAudio = new MicrophoneInput(_logger);
                _micAudio.Start();
                micActive = true;
            }

            // Create the writer with only the sources that are actually running.
            var effectiveOptions = new AudioCaptureOptions
            {
                OutputPath = options.OutputPath,
                SampleRate = options.SampleRate,
                ChannelCount = options.ChannelCount,
                EncoderBitRate = options.EncoderBitRate,
                CaptureSystemAudio = systemAudioActive,
                CaptureMicrophone = micActive,
            };
            _writer = new AudioFileWriter(effectiveOptions, _logger);

            if (systemAudioActive)
                _systemAudio!.SampleBufferReceived += buf => _writer?.AppendSystemAudio(buf);

            if (micActive)
                _micAudio!.SampleBufferReceived += buf => _writer?.AppendMicAudio(buf);

            SetState(AudioCaptureState.Recording);
            _logger.LogInformation("AudioCaptureService: recording started → {Path}", options.OutputPath);
        }
        catch
        {
            await CleanupSourcesAsync();
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
            {
                return new AudioCaptureResult
                {
                    Success = false,
                    ErrorMessage = $"Cannot stop — current state is {_state}."
                };
            }

            SetState(AudioCaptureState.Stopping);

            await CleanupSourcesAsync();

            var result = _writer is not null
                ? await _writer.FinishAsync()
                : new AudioCaptureResult { Success = false, ErrorMessage = "Writer was not initialised." };

            _writer?.Dispose();
            _writer = null;
            _activeOptions = null;

            SetState(AudioCaptureState.Idle);
            _logger.LogInformation("AudioCaptureService: recording stopped. Success={Success}", result.Success);
            return result;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task CleanupSourcesAsync()
    {
        if (_systemAudio is not null)
        {
            await _systemAudio.StopAsync();
            _systemAudio.Dispose();
            _systemAudio = null;
        }

        _micAudio?.Stop();
        _micAudio?.Dispose();
        _micAudio = null;
    }

    private void SetState(AudioCaptureState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        _systemAudio?.Dispose();
        _micAudio?.Dispose();
        _writer?.Dispose();
        _stateLock.Dispose();
    }
}
