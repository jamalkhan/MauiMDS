namespace MauiMds.AudioCapture;

public sealed class AudioCaptureOptions
{
    /// <summary>Full path for the output .m4a file.</summary>
    public string OutputPath { get; init; } = string.Empty;

    public int SampleRate { get; init; } = 48_000;
    public int ChannelCount { get; init; } = 2;
    public int EncoderBitRate { get; init; } = 128_000;

    /// <summary>Capture audio from other running apps (Teams, Zoom, etc.) via ScreenCaptureKit.</summary>
    public bool CaptureSystemAudio { get; init; } = true;

    /// <summary>Capture the local microphone via AVCaptureSession.</summary>
    public bool CaptureMicrophone { get; init; } = true;
}
