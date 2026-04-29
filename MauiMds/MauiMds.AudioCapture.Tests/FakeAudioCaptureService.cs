namespace MauiMds.AudioCapture.Tests;

/// <summary>
/// In-process test double for IAudioCaptureService.
/// Simulates the same state-machine transitions as the real Mac implementation
/// without touching any platform APIs.
/// </summary>
internal sealed class FakeAudioCaptureService : IAudioCaptureService
{
    private AudioCaptureState _state = AudioCaptureState.Idle;

    public AudioCaptureState State => _state;
    public string? LastStartWarning { get; set; }
    public event EventHandler<AudioCaptureState>? StateChanged;

    // Configuration knobs
    public bool ShouldThrowOnStart { get; set; }
    public bool ShouldFailStop { get; set; }
    public AudioPermissionStatus MicrophonePermission { get; set; } = AudioPermissionStatus.Granted;

    // Inspection
    public AudioCaptureOptions? LastOptions { get; private set; }
    public int StartCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public List<AudioCaptureState> StateHistory { get; } = [];

    public Task<AudioPermissionStatus> CheckMicrophonePermissionAsync()
        => Task.FromResult(MicrophonePermission);

    public Task<AudioPermissionStatus> RequestMicrophonePermissionAsync()
        => Task.FromResult(MicrophonePermission == AudioPermissionStatus.NotDetermined
            ? AudioPermissionStatus.Granted
            : MicrophonePermission);

    public Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowOnStart)
            throw new InvalidOperationException("Simulated start failure.");

        if (_state != AudioCaptureState.Idle)
            throw new InvalidOperationException($"Cannot start from state {_state}.");

        StartCallCount++;
        LastOptions = options;
        Transition(AudioCaptureState.Starting);
        Transition(AudioCaptureState.Recording);
        return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync()
    {
        StopCallCount++;

        if (_state != AudioCaptureState.Recording)
        {
            return Task.FromResult(new AudioCaptureResult
            {
                Success = false,
                ErrorMessage = $"Cannot stop from state {_state}."
            });
        }

        Transition(AudioCaptureState.Stopping);
        Transition(AudioCaptureState.Idle);

        var paths = new List<string>();
        if (!string.IsNullOrEmpty(LastOptions?.OutputPath))
            paths.Add(LastOptions.OutputPath);
        if (!string.IsNullOrEmpty(LastOptions?.SysOutputPath))
            paths.Add(LastOptions.SysOutputPath);

        return Task.FromResult(new AudioCaptureResult
        {
            Success = !ShouldFailStop,
            AudioFilePaths = ShouldFailStop ? [] : paths,
            Duration = TimeSpan.FromSeconds(5),
            ErrorMessage = ShouldFailStop ? "Simulated stop failure." : null
        });
    }

    private void Transition(AudioCaptureState newState)
    {
        _state = newState;
        StateHistory.Add(newState);
        StateChanged?.Invoke(this, newState);
    }
}
