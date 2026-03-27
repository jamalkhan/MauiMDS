using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MauiMds.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeLock = new();
    private bool _disposed;

    public FileLoggerProvider(string logFilePath, LogLevel minimumLevel = LogLevel.Information)
    {
        _logFilePath = logFilePath;
        _minimumLevel = minimumLevel;

        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _logFilePath, _minimumLevel, _writeLock));
    }

    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logFilePath;
    private readonly LogLevel _minimumLevel;
    private readonly object _writeLock;

    public FileLogger(string categoryName, string logFilePath, LogLevel minimumLevel, object writeLock)
    {
        _categoryName = categoryName;
        _logFilePath = logFilePath;
        _minimumLevel = minimumLevel;
        _writeLock = writeLock;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= _minimumLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var line = $"{DateTimeOffset.Now:O} [{logLevel}] {_categoryName}: {message}";
        if (exception is not null)
        {
            line += $"{Environment.NewLine}{exception}";
        }

        lock (_writeLock)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
