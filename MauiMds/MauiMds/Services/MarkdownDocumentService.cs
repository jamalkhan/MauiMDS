using MauiMds.Models;

namespace MauiMds.Services;

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    private readonly IMarkdownFileStorageService _storageService;
    private readonly IMarkdownDocumentPickerService _pickerService;
    private readonly IMarkdownFileAccessService _fileAccessService;

    public MarkdownDocumentService(
        IMarkdownFileStorageService storageService,
        IMarkdownDocumentPickerService pickerService,
        IMarkdownFileAccessService fileAccessService)
    {
        _storageService = storageService;
        _pickerService = pickerService;
        _fileAccessService = fileAccessService;
    }

    public Task<MarkdownDocument?> LoadInitialDocumentAsync()
    {
        return _storageService.LoadInitialDocumentAsync();
    }

    public Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _storageService.LoadDocumentAsync(filePath, cancellationToken);
    }

    public Task<string?> PickDocumentPathAsync()
    {
        return _pickerService.PickDocumentPathAsync();
    }

    public async Task<MarkdownDocument?> PickDocumentAsync()
    {
        var filePath = await PickDocumentPathAsync();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return await _storageService.LoadDocumentAsync(filePath);
    }

    public Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null)
    {
        return _storageService.CreateUntitledDocumentAsync(suggestedName);
    }

    public async Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
    {
        if (document.IsUntitled || string.IsNullOrWhiteSpace(document.FilePath))
        {
            var saveAsResult = await SaveAsAsync(document, cancellationToken);
            return saveAsResult ?? throw new InvalidOperationException("Save was canceled.");
        }

        return await _storageService.SaveAsync(document, cancellationToken);
    }

    public async Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
    {
        var suggestedFileName = MarkdownFileConventions.EnsureValidFileName(document.FileName, allowEmpty: true);
        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            suggestedFileName = "Untitled.mds";
        }

        suggestedFileName = MarkdownFileConventions.EnsureMarkdownExtension(suggestedFileName);
        return await _pickerService.SaveAsAsync(document, suggestedFileName, cancellationToken);
    }

    public string? TryCreatePersistentAccessBookmark(string filePath)
    {
        return _fileAccessService.TryCreatePersistentAccessBookmark(filePath);
    }

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
        return _fileAccessService.TryRestorePersistentAccessFromBookmark(bookmark, out restoredPath, out isStale);
    }
}
