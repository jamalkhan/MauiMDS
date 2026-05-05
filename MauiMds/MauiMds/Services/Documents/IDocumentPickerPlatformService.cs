using MauiMds.Models;

namespace MauiMds.Services;

public interface IDocumentPickerPlatformService
{
    Task<string?> PickDocumentPathAsync();
    Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken ct = default);
}
