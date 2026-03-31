using System.Text;
using MauiMds.Models;
using Microsoft.Extensions.Logging;
#if MACCATALYST
using Foundation;
using UniformTypeIdentifiers;
using UIKit;
#endif
#if WINDOWS
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
#endif

namespace MauiMds.Services;

public sealed class MarkdownDocumentPickerService : IMarkdownDocumentPickerService
{
    private readonly MarkdownFileAccessService _fileAccessService;
    private readonly ILogger<MarkdownDocumentPickerService> _logger;

    public MarkdownDocumentPickerService(MarkdownFileAccessService fileAccessService, ILogger<MarkdownDocumentPickerService> logger)
    {
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<string?> PickDocumentPathAsync()
    {
        _logger.LogInformation("Opening file picker for markdown documents.");

#if MACCATALYST
        var pickedUrl = await PickDocumentUrlMacCatalystAsync();
        if (pickedUrl is null)
        {
            return null;
        }

        var fullPath = pickedUrl.Path ?? string.Empty;
        _fileAccessService.TrackUrl(fullPath, pickedUrl);
        return fullPath;
#else
        var options = new PickOptions
        {
            PickerTitle = "Open a Markdown or MDS file"
        };

        if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
        {
            options.FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".md", ".mds"] }
            });
        }

        var result = await FilePicker.Default.PickAsync(options);
        return result?.FullPath;
#endif
    }

    public async Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, string suggestedFileName, CancellationToken cancellationToken = default)
    {
#if MACCATALYST
        return await SaveAsMacCatalystAsync(suggestedFileName, document, cancellationToken);
#elif WINDOWS
        return await SaveAsWindowsAsync(suggestedFileName, document, cancellationToken);
#else
        throw new PlatformNotSupportedException("Save As is only implemented for desktop platforms.");
#endif
    }

#if MACCATALYST
    private async Task<NSUrl?> PickDocumentUrlMacCatalystAsync()
    {
        var picker = new UIDocumentPickerViewController(
            [CreateContentType("public.plain-text"), CreateContentType("public.text"), CreateContentType("public.data")],
            asCopy: false)
        {
            AllowsMultipleSelection = false
        };

        return await PresentPickerAsync(picker);
    }

    private async Task<SaveDocumentResult?> SaveAsMacCatalystAsync(string suggestedFileName, EditorDocumentState document, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), suggestedFileName);
        await File.WriteAllTextAsync(
            tempPath,
            MarkdownFileConventions.NormalizeNewLines(document.Content, document.NewLine),
            MarkdownFileConventions.ResolveEncoding(document.EncodingName),
            cancellationToken);

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
        _fileAccessService.TrackUrl(savedPath, pickedUrl);

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
        var pickerDelegate = new MarkdownDocumentPickerDelegate(tcs);
        picker.Delegate = pickerDelegate;
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

    private sealed class MarkdownDocumentPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly TaskCompletionSource<NSUrl?> _tcs;

        public MarkdownDocumentPickerDelegate(TaskCompletionSource<NSUrl?> tcs)
        {
            _tcs = tcs;
        }

        public override void WasCancelled(UIDocumentPickerViewController controller)
        {
            _tcs.TrySetResult(null);
        }

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            _tcs.TrySetResult(urls.FirstOrDefault());
        }
    }
#endif

#if WINDOWS
    private static async Task<SaveDocumentResult?> SaveAsWindowsAsync(string suggestedFileName, EditorDocumentState document, CancellationToken cancellationToken)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
        };

        picker.FileTypeChoices.Add("Markdown files", [".md", ".mds"]);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        InitializeWithWindow.Initialize(picker, GetWindowsWindowHandle());

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await FileIO.WriteTextAsync(file, MarkdownFileConventions.NormalizeNewLines(document.Content, document.NewLine));
        var properties = await file.GetBasicPropertiesAsync();

        return new SaveDocumentResult
        {
            FilePath = file.Path,
            FileName = file.Name,
            FileSizeBytes = (long)properties.Size,
            LastModified = file.DateCreated
        };
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
