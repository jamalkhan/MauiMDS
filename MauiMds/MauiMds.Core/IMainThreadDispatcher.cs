namespace MauiMds;

public interface IMainThreadDispatcher
{
    void BeginInvokeOnMainThread(Action action);
    Task InvokeOnMainThreadAsync(Action action);
    Task InvokeOnMainThreadAsync(Func<Task> action);
}
