using System.ComponentModel;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public interface ISnackbarService : INotifyPropertyChanged
{
    SnackbarHistoryCollection History { get; }
    int HistoryCapacity { get; set; }
    SnackbarMessage? CurrentMessage { get; }
    bool HasCurrentMessage { get; }
    void EnqueueLog(LogLevel logLevel, string category, string message, Exception? exception = null);
    void EnqueueMessage(SnackbarMessageLevel level, string category, string message, string? exceptionMessage = null, string? exceptionDetails = null);
    void DismissVisibleAndPendingMessages();
}
