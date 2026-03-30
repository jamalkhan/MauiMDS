namespace MauiMds.Models;

public sealed class EditorPreferences
{
    public bool AutoSaveEnabled { get; init; } = true;
    public int AutoSaveDelaySeconds { get; init; } = 30;
    public int MaxLogFileSizeMb { get; init; } = 2;
}
