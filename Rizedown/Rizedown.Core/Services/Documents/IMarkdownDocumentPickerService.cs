using Rizedown.Models;

namespace Rizedown.Services;

public interface IMarkdownDocumentPickerService
{
    Task<string?> PickDocumentPathAsync();
    Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken cancellationToken = default);
}
