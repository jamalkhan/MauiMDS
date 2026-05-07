namespace MauiMds.AudioCapture;

public interface IAudioFormatConverter
{
    Task<AudioCaptureResult> ConvertAsync(
        string sourcePath, string targetPath, TimeSpan duration, int bitRate = 128_000);
}
