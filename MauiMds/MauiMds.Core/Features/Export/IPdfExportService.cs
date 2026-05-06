using MauiMds.Models;

namespace MauiMds.Features.Export;

public interface IPdfExportService
{
    Task<bool> ExportAsync(
        IReadOnlyList<MarkdownBlock> blocks,
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
