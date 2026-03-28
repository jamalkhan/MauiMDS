using MauiMds.Models;
using Microsoft.Extensions.Logging;
#if MACCATALYST
using Foundation;
using UniformTypeIdentifiers;
using UIKit;
#endif

namespace MauiMds.Services;

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    private static readonly string[] AllowedExtensions = [".mds", ".md"];
    private const string ExampleDocumentName = "example.mds";

    private readonly ILogger<MarkdownDocumentService> _logger;

    public MarkdownDocumentService(ILogger<MarkdownDocumentService> logger)
    {
        _logger = logger;
    }

    public async Task<MarkdownDocument?> LoadInitialDocumentAsync()
    {
        _logger.LogInformation("Loading initial markdown document from the app package: {DocumentName}", ExampleDocumentName);

        try
        {
            await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(ExampleDocumentName);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(content);

            _logger.LogInformation(
                "Loaded bundled markdown document. FileName: {FileName}, SizeKB: {SizeKb:F2}, ContentLength: {ContentLength}",
                ExampleDocumentName,
                sizeInBytes / 1024d,
                content.Length);

            return new MarkdownDocument
            {
                FilePath = ExampleDocumentName,
                FileName = ExampleDocumentName,
                FileSizeBytes = sizeInBytes,
                Content = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open bundled markdown document {DocumentName}", ExampleDocumentName);
            throw;
        }
    }

    public async Task<MarkdownDocument?> PickDocumentAsync()
    {
        _logger.LogDebug("Opening file picker for markdown documents.");

#if MACCATALYST
        var macCatalystDocument = await PickDocumentMacCatalystAsync();
        if (macCatalystDocument is not null)
        {
            return macCatalystDocument;
        }

        _logger.LogError("File picker canceled.");
        return null;
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
        else if (DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst)
        {
            options.FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                // Mac Catalyst expects Apple Uniform Type Identifiers rather than file extensions.
                // We allow generic data as a fallback so custom .mds files can still be chosen,
                // and we validate the final extension after selection.
                { DevicePlatform.MacCatalyst, ["net.daringfireball.markdown", "public.plain-text", "public.text", "public.data"] }
            });
        }

        var result = await FilePicker.Default.PickAsync(options);

        if (result is null)
        {
            _logger.LogError("File picker canceled.");
            return null;
        }

        var extension = Path.GetExtension(result.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected unsupported file type: {FileName}", result.FileName);
            throw new InvalidOperationException("Please choose a .mds or .md file.");
        }

        FileInfo? fileInfo = null;
        if (!string.IsNullOrWhiteSpace(result.FullPath))
        {
            fileInfo = new FileInfo(result.FullPath);
        }

        _logger.LogDebug(
            "Reading selected markdown document. FileName: {FileName}, FullPath: {FullPath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}",
            result.FileName,
            result.FullPath,
            (fileInfo?.Length ?? 0) / 1024d,
            fileInfo?.LastWriteTime);

        await using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        return new MarkdownDocument
        {
            FilePath = result.FullPath,
            FileName = result.FileName,
            FileSizeBytes = fileInfo?.Length,
            LastModified = fileInfo?.LastWriteTime,
            Content = content
        };
#endif
    }

#if MACCATALYST
    private async Task<MarkdownDocument?> PickDocumentMacCatalystAsync()
    {
        var picker = new UIDocumentPickerViewController(
            [
                CreateContentType("public.plain-text"),
                CreateContentType("public.text"),
                CreateContentType("public.data")
            ],
            asCopy: true);

        picker.AllowsMultipleSelection = false;

        var pickedUrl = await PresentPickerAsync(picker);
        if (pickedUrl is null)
        {
            return null;
        }

        var extension = Path.GetExtension(pickedUrl.LastPathComponent ?? string.Empty);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected unsupported file type: {FileName}", pickedUrl.LastPathComponent);
            throw new InvalidOperationException("Please choose a .mds or .md file.");
        }

        var fullPath = pickedUrl.Path ?? string.Empty;
        using var securityScope = new SecurityScopedResourceAccess(pickedUrl);

        FileInfo? fileInfo = null;
        if (!string.IsNullOrWhiteSpace(fullPath))
        {
            fileInfo = new FileInfo(fullPath);
        }

        _logger.LogDebug(
            "Reading selected markdown document. FileName: {FileName}, FullPath: {FullPath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}",
            pickedUrl.LastPathComponent,
            fullPath,
            (fileInfo?.Length ?? 0) / 1024d,
            fileInfo?.LastWriteTime);

        var content = await File.ReadAllTextAsync(fullPath);

        return new MarkdownDocument
        {
            FilePath = fullPath,
            FileName = pickedUrl.LastPathComponent,
            FileSizeBytes = fileInfo?.Length,
            LastModified = fileInfo?.LastWriteTime,
            Content = content
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
            var presenter = GetPresentingViewController();
            presenter?.PresentViewController(picker, true, null);
        });

        return tcs.Task;
    }

    private static UIViewController? GetPresentingViewController()
    {
        var scene = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();

        var controller = scene?
            .Windows
            .FirstOrDefault(window => window.IsKeyWindow)?
            .RootViewController;

        while (controller?.PresentedViewController is not null)
        {
            controller = controller.PresentedViewController;
        }

        return controller;
    }

    private static UTType CreateContentType(string identifier)
    {
        return UTType.CreateFromIdentifier(identifier) ?? throw new InvalidOperationException($"Unable to create content type for '{identifier}'.");
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

    private sealed class SecurityScopedResourceAccess : IDisposable
    {
        private readonly NSUrl _url;
        private readonly bool _hasAccess;

        public SecurityScopedResourceAccess(NSUrl url)
        {
            _url = url;
            _hasAccess = _url.StartAccessingSecurityScopedResource();
        }

        public void Dispose()
        {
            if (_hasAccess)
            {
                _url.StopAccessingSecurityScopedResource();
            }
        }
    }
#endif
}
