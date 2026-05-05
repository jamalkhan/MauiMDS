namespace MauiMds.Features.Export;

public interface IPdfSaveDialogService
{
    Task<bool> SaveAsync(byte[] pdfBytes, string pdfName, CancellationToken ct = default);
}
