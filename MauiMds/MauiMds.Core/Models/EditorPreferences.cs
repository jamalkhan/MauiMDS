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

    public static readonly IReadOnlyList<KeyboardShortcutDefinition> DefaultShortcuts =
    [
        new KeyboardShortcutDefinition { Action = EditorActionType.Header1, Key = "1" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Header2, Key = "2" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Header3, Key = "3" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Bold,    Key = "B" },
        new KeyboardShortcutDefinition { Action = EditorActionType.Italic,  Key = "I" },
    ];
}
