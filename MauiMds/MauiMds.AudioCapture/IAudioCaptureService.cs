namespace MauiMds.AudioCapture;

public interface IAudioCaptureService
{
    AudioCaptureState State { get; }

    event EventHandler<AudioCaptureState>? StateChanged;

    /// <summary>
    /// Checks microphone permission without prompting. Screen recording permission
    /// cannot be checked programmatically — the OS prompts on first capture attempt.
    /// </summary>
    Task<AudioPermissionStatus> CheckMicrophonePermissionAsync();

    /// <summary>
    /// Requests microphone permission, showing the system dialog if needed.
    /// </summary>
    Task<AudioPermissionStatus> RequestMicrophonePermissionAsync();

    /// <summary>
    /// Starts recording to the path specified in <paramref name="options"/>.
    /// Throws if already recording or if permissions are denied.
    /// </summary>
    Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops recording, finalises the M4A file, and returns the result.
    /// </summary>
    Task<AudioCaptureResult> StopAsync();
}
