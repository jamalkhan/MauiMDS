using Foundation;
using MauiMds.Features.Export;
using UIKit;

namespace MauiMds.Features.Export;

internal sealed class PdfSaveDialogService : IPdfSaveDialogService
{
    public async Task<bool> SaveAsync(byte[] pdfBytes, string pdfName, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), pdfName);
        await File.WriteAllBytesAsync(tempPath, pdfBytes, ct);

        var tempUrl = NSUrl.CreateFileUrl(tempPath, null);
        var picker = new UIDocumentPickerViewController([tempUrl], asCopy: true)
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
}
