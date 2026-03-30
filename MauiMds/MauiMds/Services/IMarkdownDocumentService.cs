using MauiMds.Models;

namespace MauiMds.Services;

public interface IMarkdownDocumentService
{
    Task<MarkdownDocument?> LoadInitialDocumentAsync();
    Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<MarkdownDocument?> PickDocumentAsync();
    Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null);
    Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default);
    Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, CancellationToken cancellationToken = default);
}
