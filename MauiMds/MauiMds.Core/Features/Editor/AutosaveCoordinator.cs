using MauiMds.Services;

namespace MauiMds.Features.Editor;

public sealed class AutosaveCoordinator : IAutosaveCoordinator
{
    private readonly IDelayScheduler _delayScheduler;
    private readonly IMainThreadDispatcher _dispatcher;
    private CancellationTokenSource? _autosaveCancellationSource;

    public AutosaveCoordinator(IDelayScheduler delayScheduler, IMainThreadDispatcher dispatcher)
    {
        _delayScheduler = delayScheduler;
        _dispatcher = dispatcher;
    }

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
                await _delayScheduler.DelayAsync(delay, token);
                token.ThrowIfCancellationRequested();
                await _dispatcher.InvokeOnMainThreadAsync(saveAction);
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
