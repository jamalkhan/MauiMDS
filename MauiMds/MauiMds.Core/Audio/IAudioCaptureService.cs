namespace MauiMds.AudioCapture;

/// <summary>Well-known values for <see cref="IAudioCaptureService.LastStartWarning"/>.</summary>
public static class AudioCaptureWarnings
{
    /// <summary>Mac: Screen Recording permission was denied; system audio capture is unavailable.</summary>
    public const string ScreenRecordingDenied = "screen_recording_denied";

    /// <summary>Windows: WASAPI loopback initialisation failed; system audio capture is unavailable.</summary>
    public const string WasapiLoopbackUnavailable = "wasapi_loopback_unavailable";
}

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
