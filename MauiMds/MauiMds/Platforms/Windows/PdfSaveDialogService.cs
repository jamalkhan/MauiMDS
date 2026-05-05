using MauiMds.Features.Export;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MauiMds.Features.Export;

internal sealed class PdfSaveDialogService : IPdfSaveDialogService
{
    public async Task<bool> SaveAsync(byte[] pdfBytes, string pdfName, CancellationToken ct = default)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(pdfName)
        };
        picker.FileTypeChoices.Add("PDF files", [".pdf"]);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        InitializeWithWindow.Initialize(picker, GetWindowHandle());

        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;

        await File.WriteAllBytesAsync(file.Path, pdfBytes, ct);
        return true;
    }

    private static nint GetWindowHandle()
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("Unable to locate the current window.");
        var mauiWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Unable to locate the native window.");
        return WindowNative.GetWindowHandle(mauiWindow);
    }
}
