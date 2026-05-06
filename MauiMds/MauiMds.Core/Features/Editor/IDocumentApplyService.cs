using MauiMds.Models;

namespace MauiMds.Features.Editor;

public interface IDocumentApplyService
{
    DocumentApplyResult PrepareApply(EditorDocumentState currentState, MarkdownDocument document);
}
