using MauiMds.Models;

namespace MauiMds.Features.Export;

public interface IPdfExportService
{
    /// <summary>
    /// Shows a platform save dialog, renders the blocks to PDF, and writes the file.
    /// Returns true if the export completed, false if the user cancelled the dialog.
    /// </summary>
    Task<bool> ExportAsync(
        IReadOnlyList<MarkdownBlock> blocks,
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
