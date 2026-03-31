namespace MauiMds.Features.Editor;

public sealed class AutosaveCoordinator : IDisposable
{
    private CancellationTokenSource? _autosaveCancellationSource;

    public void Schedule(
        bool isEnabled,
        bool isUntitled,
        bool isDirty,
        string? filePath,
        TimeSpan delay,
        Func<Task> saveAction)
    {
        Cancel();

        if (!isEnabled || isUntitled || !isDirty || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _autosaveCancellationSource = new CancellationTokenSource();
        var token = _autosaveCancellationSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                token.ThrowIfCancellationRequested();
                await MainThread.InvokeOnMainThreadAsync(saveAction);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    public void Cancel()
    {
        _autosaveCancellationSource?.Cancel();
        _autosaveCancellationSource?.Dispose();
        _autosaveCancellationSource = null;
    }

    public void Dispose()
    {
        Cancel();
    }
}
