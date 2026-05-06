namespace MauiMds.AudioCapture;

public sealed class AudioCaptureResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> AudioFilePaths { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string FilePath => AudioFilePaths.Count > 0 ? AudioFilePaths[0] : string.Empty;
}
