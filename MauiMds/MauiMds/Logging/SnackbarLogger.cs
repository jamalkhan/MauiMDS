using System.Collections.Concurrent;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.Logging;

public sealed class SnackbarLoggerProvider : ILoggerProvider
{
    private readonly SnackbarService _snackbarService;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, SnackbarLogger> _loggers = new();
    private bool _disposed;

    public SnackbarLoggerProvider(SnackbarService snackbarService, LogLevel minimumLevel = LogLevel.Information)
    {
        _snackbarService = snackbarService;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loggers.GetOrAdd(categoryName, name => new SnackbarLogger(name, _snackbarService, _minimumLevel));
    }

    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }
}

internal sealed class SnackbarLogger : ILogger
{
    private readonly string _categoryName;
    private readonly SnackbarService _snackbarService;
    private readonly LogLevel _minimumLevel;

    public SnackbarLogger(string categoryName, SnackbarService snackbarService, LogLevel minimumLevel)
    {
        _categoryName = categoryName;
        _snackbarService = snackbarService;
        _minimumLevel = minimumLevel;
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

        _snackbarService.EnqueueLog(logLevel, _categoryName, formatter(state, exception), exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
