using MauiMds.Features.Editor;
using MauiMds.Models;

namespace MauiMds.Tests.TestHelpers;

internal sealed class FakeSynchronousDispatcher : IMainThreadDispatcher
{
    public void BeginInvokeOnMainThread(Action action) => action();
    public Task InvokeOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
    public Task InvokeOnMainThreadAsync(Func<Task> action) => action();
}

internal sealed class FakeAutosaveCoordinator : IAutosaveCoordinator
{
    public record ScheduleCall(bool IsEnabled, bool IsUntitled, bool IsDirty, string? FilePath, TimeSpan Delay);

    public List<ScheduleCall> Calls { get; } = [];
    public int CancelCount { get; private set; }

    public void Schedule(bool isEnabled, bool isUntitled, bool isDirty, string? filePath, TimeSpan delay, Func<Task> saveAction)
        => Calls.Add(new ScheduleCall(isEnabled, isUntitled, isDirty, filePath, delay));

    public void Cancel() => CancelCount++;
    public void Dispose() => Cancel();
}

internal sealed class FakeDocumentApplyService : IDocumentApplyService
{
    private readonly IDocumentWorkflowService _workflow;
    public FakeDocumentApplyService(IDocumentWorkflowService workflow) => _workflow = workflow;

    public DocumentApplyResult PrepareApply(EditorDocumentState currentState, MarkdownDocument document)
    {
        var next = _workflow.CreateDocumentState(document);
        return new DocumentApplyResult
        {
            DocumentState = next,
            FilePathChanged = !string.Equals(currentState.FilePath, next.FilePath, StringComparison.Ordinal),
            FileNameChanged = !string.Equals(currentState.FileName, next.FileName, StringComparison.Ordinal),
            IsDirtyChanged = currentState.IsDirty != next.IsDirty,
            IsUntitledChanged = currentState.IsUntitled != next.IsUntitled,
            ShouldWatchDocument = !next.IsUntitled,
            WatchFilePath = !next.IsUntitled ? next.FilePath : null
        };
    }
}
