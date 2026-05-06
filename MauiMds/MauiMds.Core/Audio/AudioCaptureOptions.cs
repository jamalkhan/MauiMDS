namespace MauiMds.AudioCapture;

public sealed class AudioCaptureOptions
{
    public string OutputPath { get; init; } = string.Empty;
    public string SysOutputPath { get; init; } = string.Empty;
    public int SampleRate { get; init; } = 48_000;
    public int ChannelCount { get; init; } = 2;
    public int EncoderBitRate { get; init; } = 128_000;
    public bool CaptureSystemAudio => !string.IsNullOrEmpty(SysOutputPath);
    public bool CaptureMicrophone => !string.IsNullOrEmpty(OutputPath);
}
