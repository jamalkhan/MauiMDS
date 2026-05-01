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
    /// Non-null when the last <see cref="StartAsync"/> succeeded but with degraded capability.
    /// "screen_recording_denied" means system audio fell back to mic-only due to missing TCC permission.
    /// Reset to null at the start of each <see cref="StartAsync"/> call.
    /// </summary>
    string? LastStartWarning { get; }

    /// <summary>
    /// Starts recording to the path specified in <paramref name="options"/>.
    /// Throws if already recording or if permissions are denied.
    /// When system audio is unavailable but microphone is enabled, falls back to mic-only
    /// and sets <see cref="LastStartWarning"/> instead of throwing.
    /// </summary>
    Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops recording, finalises the M4A file, and returns the result.
    /// </summary>
    Task<AudioCaptureResult> StopAsync();
}
