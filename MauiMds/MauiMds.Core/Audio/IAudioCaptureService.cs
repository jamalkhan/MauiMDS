namespace MauiMds.AudioCapture;

public interface IAudioCaptureService
{
    AudioCaptureState State { get; }
    event EventHandler<AudioCaptureState>? StateChanged;

    /// <summary>
    /// Raised during an active recording whenever a short WAV chunk is ready for live
    /// transcription. Only fires when <see cref="AudioCaptureOptions.EnableLiveChunks"/> is true.
    /// </summary>
    event EventHandler<LiveAudioChunk>? LiveChunkAvailable;

    string? LastStartWarning { get; }
    Task<AudioPermissionStatus> CheckMicrophonePermissionAsync();
    Task<AudioPermissionStatus> RequestMicrophonePermissionAsync();
    Task StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken = default);
    Task<AudioCaptureResult> StopAsync();
}
