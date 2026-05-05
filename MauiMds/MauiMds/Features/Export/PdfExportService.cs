using System.Diagnostics;
using MauiMds.Models;
using MauiMds.Pdf;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Export;

public sealed class PdfExportService(IPdfSaveDialogService saveDialog, ILogger<PdfExportService> logger) : IPdfExportService
{
    private readonly ILogger<PdfExportService> _logger = logger;

    public async Task<bool> ExportAsync(
        IReadOnlyList<MarkdownBlock> blocks,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        var overall = Stopwatch.StartNew();
        var pdfName = Path.ChangeExtension(Path.GetFileNameWithoutExtension(suggestedFileName), ".pdf");

        _logger.LogInformation(
            "PDF export started. BlockCount: {BlockCount}, SuggestedName: {SuggestedName}",
            blocks.Count, pdfName);

        var buildSw = Stopwatch.StartNew();
        byte[] pdfBytes;
        int pageCount;

        try
        {
            var document = new PdfDocument();
            new MarkdownPdfRenderer().Render(document, blocks);
            pdfBytes  = document.ToBytes();
            pageCount = document.Pages.Count;
            buildSw.Stop();

            _logger.LogDebug(
                "PDF rendered. Pages: {Pages}, SizeBytes: {SizeBytes}, BuildElapsedMs: {BuildMs}",
                pageCount, pdfBytes.Length, buildSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF render failed after {ElapsedMs}ms.", buildSw.ElapsedMilliseconds);
            throw;
        }

        bool saved;
        try
        {
            saved = await saveDialog.SaveAsync(pdfBytes, pdfName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF save dialog or file write failed.");
            throw;
        }

        overall.Stop();

        if (saved)
        {
            _logger.LogInformation(
                "PDF export completed. Name: {Name}, Pages: {Pages}, SizeBytes: {SizeBytes}, TotalElapsedMs: {TotalMs}",
                pdfName, pageCount, pdfBytes.Length, overall.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogDebug("PDF export cancelled by user.");
        }

        return saved;
    }
}
