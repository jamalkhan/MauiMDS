namespace MauiMds.AudioCapture;

public enum AudioCaptureSource { Microphone, System }

/// <summary>
/// A short WAV audio segment produced by the capture service during an active recording.
/// The consumer is responsible for deleting <see cref="WavFilePath"/> when done.
/// </summary>
public sealed record LiveAudioChunk(
    string WavFilePath,
    TimeSpan StartOffset,
    bool IsLastChunk,
    AudioCaptureSource Source);
