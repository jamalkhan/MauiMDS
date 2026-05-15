using Rizedown.Models;

namespace Rizedown.Features.Editor;

public interface IPreviewPipelineCoordinator : IDisposable
{
    void MarkSaved();
    bool ShouldSuppressExternalReload(bool isSavingDocument, TimeSpan suppressionWindow);
    Task SchedulePreviewAsync(
        MarkdownDocument snapshot,
        EditorViewMode currentViewMode,
        TimeSpan delay,
        Func<MarkdownDocument, DocumentPreviewResult, TimeSpan, Task> applyPreviewAsync);
    Task ScheduleExternalReloadAsync(TimeSpan delay, Func<Task> reloadAsync);
    void CancelPreview();
    void CancelExternalReload();
}
