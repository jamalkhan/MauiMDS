using System.Diagnostics;
using MauiMds.Models;
using MauiMds.Pdf;
using Microsoft.Extensions.Logging;
#if MACCATALYST
using Foundation;
using UniformTypeIdentifiers;
using UIKit;
#endif
#if WINDOWS
using Windows.Storage.Pickers;
using WinRT.Interop;
#endif

namespace MauiMds.Features.Export;

public sealed class PdfExportService(ILogger<PdfExportService> logger) : IPdfExportService
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

        // ── Render PDF ────────────────────────────────────────────────────────
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

        // ── Platform save (dialog + write) ────────────────────────────────────
        bool saved;
        try
        {
            saved = await SaveWithDialogAsync(pdfBytes, pdfName, cancellationToken);
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

    // ── Platform implementations ──────────────────────────────────────────────

    private static Task<bool> SaveWithDialogAsync(byte[] pdfBytes, string pdfName, CancellationToken ct)
    {
#if MACCATALYST
        return SaveMacCatalystAsync(pdfBytes, pdfName);
#elif WINDOWS
        return SaveWindowsAsync(pdfBytes, pdfName, ct);
#else
        throw new PlatformNotSupportedException("PDF export is only implemented for macOS and Windows.");
#endif
    }

#if MACCATALYST
    private static async Task<bool> SaveMacCatalystAsync(byte[] pdfBytes, string pdfName)
    {
        // Write real bytes to temp file first; the picker copies from there to the user's destination.
        var tempPath = Path.Combine(Path.GetTempPath(), pdfName);
        await File.WriteAllBytesAsync(tempPath, pdfBytes);

        var tempUrl = NSUrl.CreateFileUrl(tempPath, null);
        var picker  = new UIDocumentPickerViewController([tempUrl], asCopy: true)
        {
            AllowsMultipleSelection = false
        };

        var tcs = new TaskCompletionSource<bool>();
        picker.Delegate = new PdfPickerDelegate(tcs);
        picker.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;

        MainThread.BeginInvokeOnMainThread(() =>
            GetPresentingViewController()?.PresentViewController(picker, true, null));

        return await tcs.Task;
    }

    private static UIViewController? GetPresentingViewController()
    {
        var scene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>().FirstOrDefault();
        var vc = scene?.Windows.FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
        while (vc?.PresentedViewController is not null) vc = vc.PresentedViewController;
        return vc;
    }

    private sealed class PdfPickerDelegate(TaskCompletionSource<bool> tcs) : UIDocumentPickerDelegate
    {
        public override void WasCancelled(UIDocumentPickerViewController controller)
            => tcs.TrySetResult(false);

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
            => tcs.TrySetResult(urls.Length > 0);
    }
#endif

#if WINDOWS
    private static async Task<bool> SaveWindowsAsync(byte[] pdfBytes, string pdfName, CancellationToken ct)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(pdfName)
        };
        picker.FileTypeChoices.Add("PDF files", [".pdf"]);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        InitializeWithWindow.Initialize(picker, GetWindowsWindowHandle());

        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;

        await File.WriteAllBytesAsync(file.Path, pdfBytes, ct);
        return true;
    }

    private static nint GetWindowsWindowHandle()
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("Unable to locate the current window.");
        var mauiWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Unable to locate the native window.");
        return WindowNative.GetWindowHandle(mauiWindow);
    }
#endif
}
