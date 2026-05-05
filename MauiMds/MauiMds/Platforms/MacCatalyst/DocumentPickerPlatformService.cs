using System.Text;
using Foundation;
using MauiMds.Models;
using MauiMds.Services;
using UniformTypeIdentifiers;
using UIKit;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

internal sealed class DocumentPickerPlatformService : IDocumentPickerPlatformService
{
    private readonly MacMarkdownFileAccessService _fileAccess;
    private readonly ILogger<DocumentPickerPlatformService> _logger;

    public DocumentPickerPlatformService(MacMarkdownFileAccessService fileAccess, ILogger<DocumentPickerPlatformService> logger)
    {
        _fileAccess = fileAccess;
        _logger = logger;
    }

    public async Task<string?> PickDocumentPathAsync()
    {
        _logger.LogDebug("Opening file picker for markdown documents.");

        var picker = new UIDocumentPickerViewController(
            [CreateContentType("public.plain-text"), CreateContentType("public.text"), CreateContentType("public.data")],
            asCopy: false)
        {
            AllowsMultipleSelection = false
        };

        var pickedUrl = await PresentPickerAsync(picker);
        if (pickedUrl is null)
        {
            return null;
        }

        var fullPath = pickedUrl.Path ?? string.Empty;
        _fileAccess.TrackUrl(fullPath, pickedUrl);
        return fullPath;
    }

    public async Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), suggestedFileName);
        await File.WriteAllTextAsync(
            tempPath,
            MarkdownFileConventions.NormalizeNewLines(document.Content, document.NewLine),
            MarkdownFileConventions.ResolveEncoding(document.EncodingName),
            ct);

        var tempUrl = NSUrl.CreateFileUrl(tempPath, null);
        var picker = new UIDocumentPickerViewController([tempUrl], asCopy: true)
        {
            AllowsMultipleSelection = false
        };

        var pickedUrl = await PresentPickerAsync(picker);
        if (pickedUrl is null)
        {
            return null;
        }

        var savedPath = pickedUrl.Path ?? string.Empty;
        _fileAccess.TrackUrl(savedPath, pickedUrl);

        var fileInfo = new FileInfo(savedPath);
        return new SaveDocumentResult
        {
            FilePath = savedPath,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : Encoding.UTF8.GetByteCount(document.Content),
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTimeOffset.UtcNow
        };
    }

    private static Task<NSUrl?> PresentPickerAsync(UIDocumentPickerViewController picker)
    {
        var tcs = new TaskCompletionSource<NSUrl?>();
        picker.Delegate = new MarkdownDocumentPickerDelegate(tcs);
        picker.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            GetPresentingViewController()?.PresentViewController(picker, true, null);
        });

        return tcs.Task;
    }

    private static UIViewController? GetPresentingViewController()
    {
        var scene = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().FirstOrDefault();
        var controller = scene?.Windows.FirstOrDefault(window => window.IsKeyWindow)?.RootViewController;

        while (controller?.PresentedViewController is not null)
        {
            controller = controller.PresentedViewController;
        }

        return controller;
    }

    private static UTType CreateContentType(string identifier)
    {
        return UTType.CreateFromIdentifier(identifier)
            ?? throw new InvalidOperationException($"Unable to create content type for '{identifier}'.");
    }

    private sealed class MarkdownDocumentPickerDelegate(TaskCompletionSource<NSUrl?> tcs) : UIDocumentPickerDelegate
    {
        public override void WasCancelled(UIDocumentPickerViewController controller)
            => tcs.TrySetResult(null);

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
            => tcs.TrySetResult(urls.FirstOrDefault());
    }
}
