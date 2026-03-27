namespace MauiMds.Logging;

public static class LogPaths
{
    public static string AppLogFilePath =>
        Path.Combine(FileSystem.Current.AppDataDirectory, "logs", "mauimds.log");
}
