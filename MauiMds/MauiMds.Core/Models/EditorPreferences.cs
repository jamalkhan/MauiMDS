using Microsoft.Extensions.Logging;

namespace MauiMds.Models;

public sealed class EditorPreferences
{
    public bool AutoSaveEnabled { get; init; } = true;
    public int AutoSaveDelaySeconds { get; init; } = 30;
    public int MaxLogFileSizeMb { get; init; } = 2;
    public int InitialViewerRenderLineCount { get; init; } = 20;
    public bool Use24HourTime { get; init; }
    public LogLevel FileLogLevel { get; init; } = LogLevel.Information;
    public IReadOnlyList<KeyboardShortcutDefinition> KeyboardShortcuts { get; init; } = DefaultShortcuts;

    public TranscriptionEngineType TranscriptionEngine { get; init; } = TranscriptionEngineType.AppleSpeech;
    public DiarizationEngineType DiarizationEngine { get; init; } = DiarizationEngineType.None;
    public string WhisperBinaryPath { get; init; } = string.Empty;
    public string WhisperModelPath { get; init; } = string.Empty;
    public string PyannotePythonPath { get; init; } = string.Empty;
    public string PyannoteHfToken { get; init; } = string.Empty;
    public RecordingFormat RecordingFormat { get; init; } = RecordingFormat.M4A;

    /// <summary>
    /// How often live transcription chunks are emitted during recording (seconds).
    /// Lower values reduce latency; minimum effective value is ~5 s.
    /// </summary>
    public int LiveChunkIntervalSeconds { get; init; } = 8;

    /// <summary>
    /// How often (in seconds) the workspace file explorer auto-refreshes from disk.
    /// 0 = disabled; a FileSystemWatcher handles instant updates regardless.
    /// </summary>
    public int WorkspaceRefreshIntervalSeconds { get; init; } = 30;

    public static readonly IReadOnlyList<KeyboardShortcutDefinition> DefaultShortcuts =
    [
        new KeyboardShortcutDefinition { Action = EditorActionType.Header1, Key = "1" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Header2, Key = "2" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Header3, Key = "3" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Bold,    Key = "B" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Italic,  Key = "I" },
    ];
}
