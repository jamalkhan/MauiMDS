using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public sealed class SnackbarService : INotifyPropertyChanged, IDisposable
{
    public static readonly TimeSpan DefaultDisplayDuration = TimeSpan.FromSeconds(5);
    public const SnackbarMessageLevel DefaultVisibleMinimumLevel = SnackbarMessageLevel.Error;

    /// <summary>
    /// Maximum number of snackbar messages retained in history.
    /// Change this default if you want to keep more or fewer historical messages.
    /// </summary>
    public const int DefaultHistoryCapacity = 100;

    private readonly ConcurrentQueue<SnackbarMessage> _pendingMessages = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _messagePumpTask;
    private SnackbarMessage? _currentMessage;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SnackbarHistoryCollection History { get; } = new();

    public TimeSpan DisplayDuration { get; set; } = DefaultDisplayDuration;

    public int HistoryCapacity { get; set; } = DefaultHistoryCapacity;

    /// <summary>
    /// Minimum level that should appear in the live Snackbar pop-up.
    /// History still keeps messages below this threshold.
    /// </summary>
    public SnackbarMessageLevel VisibleMinimumLevel { get; set; } = DefaultVisibleMinimumLevel;

    public SnackbarMessage? CurrentMessage
    {
        get => _currentMessage;
        private set
        {
            if (_currentMessage == value)
            {
                return;
            }

            _currentMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrentMessage));
        }
    }

    public bool HasCurrentMessage => CurrentMessage is not null;

    public SnackbarService()
    {
        _messagePumpTask = Task.Run(ProcessQueueAsync);
    }

    public void EnqueueLog(LogLevel logLevel, string category, string message, Exception? exception = null)
    {
        if (logLevel == LogLevel.None)
        {
            return;
        }

        Enqueue(new SnackbarMessage
        {
            Level = SnackbarMessage.FromLogLevel(logLevel),
            Category = category,
            Message = message,
            Timestamp = DateTimeOffset.Now,
            ExceptionMessage = exception?.Message,
            ExceptionDetails = exception?.ToString()
        });
    }

    public void EnqueueMessage(SnackbarMessageLevel level, string category, string message, string? exceptionMessage = null, string? exceptionDetails = null)
    {
        Enqueue(new SnackbarMessage
        {
            Level = level,
            Category = category,
            Message = message,
            Timestamp = DateTimeOffset.Now,
            ExceptionMessage = exceptionMessage,
            ExceptionDetails = exceptionDetails
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _pendingSignal.Dispose();
        _shutdown.Dispose();
    }

    public void DismissVisibleAndPendingMessages()
    {
        _pendingMessages.Clear();
        MainThread.BeginInvokeOnMainThread(() => CurrentMessage = null);
    }

    private void Enqueue(SnackbarMessage message)
    {
        MainThread.BeginInvokeOnMainThread(() => AddToHistory(message));

        if (message.Level < VisibleMinimumLevel)
        {
            return;
        }

        _pendingMessages.Enqueue(message);
        _pendingSignal.Release();
    }

    private void AddToHistory(SnackbarMessage message)
    {
        History.Add(message);

        while (History.Count > HistoryCapacity)
        {
            History.RemoveAt(0);
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await _pendingSignal.WaitAsync(_shutdown.Token);

                if (!_pendingMessages.TryDequeue(out var message))
                {
                    continue;
                }

                await MainThread.InvokeOnMainThreadAsync(() => CurrentMessage = message);
                await Task.Delay(DisplayDuration, _shutdown.Token);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (ReferenceEquals(CurrentMessage, message))
                    {
                        CurrentMessage = null;
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
