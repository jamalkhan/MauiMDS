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

    /// <summary>When true the capture service emits <see cref="IAudioCaptureService.LiveChunkAvailable"/> during recording.</summary>
    public bool EnableLiveChunks { get; init; }

    /// <summary>Approximate interval between live chunks. Defaults to 8 seconds.</summary>
    public TimeSpan LiveChunkInterval { get; init; } = TimeSpan.FromSeconds(8);
}
