namespace MauiMds.Models;

/// <summary>
/// Represents two audio files (mic + optional system audio) captured in the same session,
/// plus an optional transcript, as a single logical item in the workspace explorer.
/// </summary>
public sealed class RecordingGroup
{
    /// <summary>
    /// The shared base name, e.g. "audio_capture_2026_04_27_123456".
    /// </summary>
    public required string BaseName { get; init; }

    /// <summary>Directory that contains the files.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Full path to the microphone recording, if it exists.</summary>
    public string? MicFilePath { get; init; }

    /// <summary>Full path to the system-audio recording, if it exists.</summary>
    public string? SysFilePath { get; init; }

    /// <summary>Full path to the transcript .mds file, if it exists.</summary>
    public string? TranscriptPath { get; init; }

    /// <summary>Display-friendly label shown in the workspace tree.</summary>
    public string DisplayName => BaseName;

    /// <summary>All audio file paths that are present.</summary>
    public IReadOnlyList<string> AudioFilePaths
    {
        get
        {
            var list = new List<string>(2);
            if (MicFilePath is not null) list.Add(MicFilePath);
            if (SysFilePath is not null) list.Add(SysFilePath);
            return list;
        }
    }

    public bool HasTranscript => TranscriptPath is not null;
    public bool IsDualSource => MicFilePath is not null && SysFilePath is not null;
}
