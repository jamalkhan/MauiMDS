using MauiMds.Models;

namespace MauiMds.Features.Editor;

public interface IDocumentWorkflowService
{
    EditorDocumentState CreateDocumentState(MarkdownDocument document);
    DocumentPreviewResult PreparePreview(MarkdownDocument document, EditorViewMode currentViewMode);
    DocumentLoadResult PrepareDocument(MarkdownDocument document, EditorViewMode currentViewMode);
    void ApplySaveResult(EditorDocumentState document, SaveDocumentResult result);
}
