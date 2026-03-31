using Microsoft.Extensions.Logging;

namespace MauiMds.Models;

public enum SnackbarMessageLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed class SnackbarMessage
{
    public required SnackbarMessageLevel Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? ExceptionMessage { get; init; }
    public string? ExceptionDetails { get; init; }

    public string LevelLabel => Level switch
    {
        SnackbarMessageLevel.Debug => "Debug",
        SnackbarMessageLevel.Info => "Info",
        SnackbarMessageLevel.Warning => "Warning",
        _ => "Error"
    };

    public string DisplayMessage =>
        string.IsNullOrWhiteSpace(ExceptionMessage) || Message.Contains(ExceptionMessage, StringComparison.Ordinal)
            ? Message
            : $"{Message} {ExceptionMessage}";

    public static SnackbarMessageLevel FromLogLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => SnackbarMessageLevel.Debug,
            LogLevel.Debug => SnackbarMessageLevel.Debug,
            LogLevel.Information => SnackbarMessageLevel.Info,
            LogLevel.Warning => SnackbarMessageLevel.Warning,
            LogLevel.Error => SnackbarMessageLevel.Error,
            LogLevel.Critical => SnackbarMessageLevel.Error,
            _ => SnackbarMessageLevel.Info
        };
    }
}
