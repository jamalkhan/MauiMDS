namespace MauiMds.Logging;

public static class LogPaths
{
    public static string AppLogFilePath =>
        Path.Combine(FileSystem.Current.AppDataDirectory, "logs", "mauimds.log");

    public static string PreferencesFilePath =>
        Path.Combine(FileSystem.Current.AppDataDirectory, "preferences", "editor-preferences.json");

    public static string SessionStateFilePath =>
        Path.Combine(FileSystem.Current.AppDataDirectory, "session", "session-state.json");
}
