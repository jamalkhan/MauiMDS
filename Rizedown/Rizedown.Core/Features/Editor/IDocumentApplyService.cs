using Rizedown.Models;

namespace Rizedown.Features.Editor;

public interface IDocumentApplyService
{
    DocumentApplyResult PrepareApply(EditorDocumentState currentState, MarkdownDocument document);
}
