namespace MauiMds.AudioCapture;

/// <summary>
/// Exposes raw platform audio buffers during an active recording for engines that
/// support native streaming (e.g. Apple Speech via SFSpeechAudioBufferRecognitionRequest).
/// The event argument is the opaque native handle (CMSampleBuffer.Handle on Mac).
/// Implementations must retain the buffer for the duration of each event invocation.
/// </summary>
public interface INativeMicrophoneSource
{
    event EventHandler<nint>? NativeSampleBufferAvailable;
}
