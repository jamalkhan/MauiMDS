namespace MauiMds;

internal sealed class MauiMainThreadDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) =>
        MainThread.BeginInvokeOnMainThread(action);

    public Task InvokeOnMainThreadAsync(Action action) =>
        MainThread.InvokeOnMainThreadAsync(action);

    public Task InvokeOnMainThreadAsync(Func<Task> action) =>
        MainThread.InvokeOnMainThreadAsync(action);
}
