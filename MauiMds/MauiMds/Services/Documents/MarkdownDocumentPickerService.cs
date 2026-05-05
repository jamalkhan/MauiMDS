using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

public sealed class MarkdownDocumentPickerService : IMarkdownDocumentPickerService
{
    private readonly IDocumentPickerPlatformService _platform;
    private readonly ILogger<MarkdownDocumentPickerService> _logger;

    public MarkdownDocumentPickerService(IDocumentPickerPlatformService platform, ILogger<MarkdownDocumentPickerService> logger)
    {
        _platform = platform;
        _logger = logger;
    }

    public Task<string?> PickDocumentPathAsync() => _platform.PickDocumentPathAsync();

    public Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken cancellationToken = default)
        => _platform.SaveAsAsync(document, suggestedFileName, cancellationToken);
}
