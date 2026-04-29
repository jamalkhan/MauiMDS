namespace MauiMds.AudioCapture;

public sealed class AudioCaptureResult
{
    public bool Success { get; init; }

    /// <summary>
    /// All audio file paths produced by this recording session.
    /// Single-source: one path. Dual-source: mic path + sys path.
    /// Empty when <see cref="Success"/> is false.
    /// </summary>
    public IReadOnlyList<string> AudioFilePaths { get; init; } = [];

    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Convenience: the first (or only) audio file path.</summary>
    public string FilePath => AudioFilePaths.Count > 0 ? AudioFilePaths[0] : string.Empty;
}
