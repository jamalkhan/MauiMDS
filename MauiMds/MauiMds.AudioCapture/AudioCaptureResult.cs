namespace MauiMds.AudioCapture;

public sealed class AudioCaptureResult
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}
