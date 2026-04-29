namespace MauiMds.AudioCapture;

public sealed class AudioCaptureOptions
{
    /// <summary>Full path for the microphone output file (M4A, MP3, or FLAC).</summary>
    public string OutputPath { get; init; } = string.Empty;

    /// <summary>
    /// Full path for the system-audio output file (always M4A).
    /// When non-empty, system audio is captured to this separate file.
    /// When empty, system audio is not captured.
    /// </summary>
    public string SysOutputPath { get; init; } = string.Empty;

    public int SampleRate { get; init; } = 48_000;
    public int ChannelCount { get; init; } = 2;
    public int EncoderBitRate { get; init; } = 128_000;

    /// <summary>Capture audio from other running apps via ScreenCaptureKit.</summary>
    public bool CaptureSystemAudio => !string.IsNullOrEmpty(SysOutputPath);

    /// <summary>Capture the local microphone via AVCaptureSession.</summary>
    public bool CaptureMicrophone => !string.IsNullOrEmpty(OutputPath);
}
