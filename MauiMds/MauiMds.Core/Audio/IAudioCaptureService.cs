namespace MauiMds.AudioCapture;

public interface IAudioCaptureService
{
    AudioCaptureState State { get; }
    event EventHandler<AudioCaptureState>? StateChanged;
    string? LastStartWarning { get; }
    Task<AudioPermissionStatus> CheckMicrophonePermissionAsync();
    Task<AudioPermissionStatus> RequestMicrophonePermissionAsync();
    Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default);
    Task<AudioCaptureResult> StopAsync();
}
