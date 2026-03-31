using MauiMds.Models;

namespace MauiMds.Services;

public interface IMarkdownFileStorageService
{
    Task<MarkdownDocument?> LoadInitialDocumentAsync();
    Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null);
    Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default);
}
