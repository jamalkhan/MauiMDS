using Rizedown.Models;

namespace Rizedown.Services;

public interface IDocumentPickerPlatformService
{
    Task<string?> PickDocumentPathAsync();
    Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken ct = default);
}
