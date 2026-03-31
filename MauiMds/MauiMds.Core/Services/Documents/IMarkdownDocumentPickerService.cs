using MauiMds.Models;

namespace MauiMds.Services;

public interface IMarkdownDocumentPickerService
{
    Task<string?> PickDocumentPathAsync();
    Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken cancellationToken = default);
}
