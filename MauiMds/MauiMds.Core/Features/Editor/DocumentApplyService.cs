using MauiMds.Models;

namespace MauiMds.Features.Editor;

public sealed class DocumentApplyService : IDocumentApplyService
{
    private readonly IDocumentWorkflowService _documentWorkflowController;

    public DocumentApplyService(IDocumentWorkflowService documentWorkflowController)
    {
        _documentWorkflowController = documentWorkflowController;
    }

    public DocumentApplyResult PrepareApply(EditorDocumentState currentState, MarkdownDocument document)
    {
        var nextState = _documentWorkflowController.CreateDocumentState(document);

        return new DocumentApplyResult
        {
            DocumentState = nextState,
            FilePathChanged = !string.Equals(currentState.FilePath, nextState.FilePath, StringComparison.Ordinal),
            FileNameChanged = !string.Equals(currentState.FileName, nextState.FileName, StringComparison.Ordinal),
            IsDirtyChanged = currentState.IsDirty != nextState.IsDirty,
            IsUntitledChanged = currentState.IsUntitled != nextState.IsUntitled,
            ShouldWatchDocument = !nextState.IsUntitled && !string.IsNullOrWhiteSpace(nextState.FilePath),
            WatchFilePath = !nextState.IsUntitled && !string.IsNullOrWhiteSpace(nextState.FilePath)
                ? nextState.FilePath
                : null
        };
    }
}
