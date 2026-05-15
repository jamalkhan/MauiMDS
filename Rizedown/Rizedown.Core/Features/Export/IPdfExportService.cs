using Rizedown.Models;

namespace Rizedown.Features.Export;

public interface IPdfExportService
{
    Task<bool> ExportAsync(
        IReadOnlyList<MarkdownBlock> blocks,
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
