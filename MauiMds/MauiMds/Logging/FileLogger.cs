using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MauiMds.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly FileLogLevelSwitch _levelSwitch;
    private readonly long _maxFileSizeBytes;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeLock = new();
    private bool _disposed;

    public FileLoggerProvider(string logFilePath, FileLogLevelSwitch levelSwitch, long maxFileSizeBytes = 2 * 1024 * 1024)
    {
        _logFilePath = logFilePath;
        _levelSwitch = levelSwitch;
        _maxFileSizeBytes = Math.Max(256 * 1024, maxFileSizeBytes);

        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _logFilePath, _levelSwitch, _maxFileSizeBytes, _writeLock));
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
    private readonly FileLogLevelSwitch _levelSwitch;
    private readonly long _maxFileSizeBytes;
    private readonly object _writeLock;

    public FileLogger(string categoryName, string logFilePath, FileLogLevelSwitch levelSwitch, long maxFileSizeBytes, object writeLock)
    {
        _categoryName = categoryName;
        _logFilePath = logFilePath;
        _levelSwitch = levelSwitch;
        _maxFileSizeBytes = maxFileSizeBytes;
        _writeLock = writeLock;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= _levelSwitch.MinimumLevel;
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
            TrimLogFileIfNeeded();
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }

    private void TrimLogFileIfNeeded()
    {
        var fileInfo = new FileInfo(_logFilePath);
        if (!fileInfo.Exists || fileInfo.Length < _maxFileSizeBytes)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var retainedBytes = Math.Max(_maxFileSizeBytes / 2, 128 * 1024);
        using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytesToRead = (int)Math.Min(retainedBytes, stream.Length);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        var buffer = new byte[bytesToRead];
        _ = stream.Read(buffer, 0, bytesToRead);

        var firstNewLine = Array.IndexOf(buffer, (byte)'\n');
        var trimmedBuffer = firstNewLine >= 0 && firstNewLine + 1 < buffer.Length
            ? buffer[(firstNewLine + 1)..]
            : buffer;

        File.WriteAllBytes(_logFilePath, trimmedBuffer);
        Debug.WriteLine($"Trimmed log file {_logFilePath} to {trimmedBuffer.Length} bytes in {stopwatch.ElapsedMilliseconds} ms.");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
