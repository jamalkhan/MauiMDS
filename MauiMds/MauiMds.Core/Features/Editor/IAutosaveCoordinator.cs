namespace MauiMds.Features.Editor;

public interface IAutosaveCoordinator : IDisposable
{
    void Schedule(
        bool isEnabled,
        bool isUntitled,
        bool isDirty,
        string? filePath,
        TimeSpan delay,
        Func<Task> saveAction);
    void Cancel();
}
