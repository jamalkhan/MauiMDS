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

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    private static readonly string[] AllowedExtensions = [".mds", ".md"];
    private const string ExampleDocumentName = "example.mds";
    private readonly ILogger<MarkdownDocumentService> _logger;
#if MACCATALYST
    private readonly Dictionary<string, NSUrl> _securityScopedUrls = new(StringComparer.Ordinal);
#endif

    public MarkdownDocumentService(ILogger<MarkdownDocumentService> logger)
    {
        _logger = logger;
    }

    public async Task<MarkdownDocument?> LoadInitialDocumentAsync()
    {
        _logger.LogInformation("Loading initial markdown document from the app package: {DocumentName}", ExampleDocumentName);

        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(ExampleDocumentName);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync();

        return new MarkdownDocument
        {
            FilePath = ExampleDocumentName,
            FileName = ExampleDocumentName,
            FileSizeBytes = Encoding.UTF8.GetByteCount(content),
            Content = content,
            IsUntitled = false,
            EncodingName = reader.CurrentEncoding.WebName,
            NewLine = DetectNewLine(content)
        };
    }

    public async Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ValidateExtension(filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The markdown document could not be found.", filePath);
        }

#if MACCATALYST
        using var access = CreateSecurityScope(filePath);
#endif

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied while reading file {FilePath}", filePath);
            throw;
        }

        var encoding = DetectEncoding(bytes);
        var content = encoding.GetString(bytes);

        _logger.LogInformation(
            "Loaded markdown document. FileName: {FileName}, FilePath: {FilePath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}, Encoding: {Encoding}",
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length / 1024d,
            fileInfo.LastWriteTimeUtc,
            encoding.WebName);

        return new MarkdownDocument
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Content = content,
            IsUntitled = false,
            EncodingName = encoding.WebName,
            NewLine = DetectNewLine(content)
        };
    }

    public async Task<MarkdownDocument?> PickDocumentAsync()
    {
        _logger.LogInformation("Opening file picker for markdown documents.");

#if MACCATALYST
        var pickedUrl = await PickDocumentUrlMacCatalystAsync();
        if (pickedUrl is null)
        {
            return null;
        }

        return await LoadDocumentFromPickedUrlAsync(pickedUrl);
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
        if (result is null || string.IsNullOrWhiteSpace(result.FullPath))
        {
            _logger.LogInformation("File picker canceled.");
            return null;
        }

        return await LoadDocumentAsync(result.FullPath);
#endif
    }

    public Task<MarkdownDocument> CreateUntitledDocumentAsync(string? suggestedName = null)
    {
        var fileName = EnsureValidFileName(suggestedName, allowEmpty: true);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Untitled.mds";
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            fileName = $"{Path.GetFileNameWithoutExtension(fileName)}.mds";
        }

        return Task.FromResult(new MarkdownDocument
        {
            FilePath = string.Empty,
            FileName = fileName,
            Content = string.Empty,
            FileSizeBytes = 0,
            LastModified = null,
            IsUntitled = true,
            EncodingName = Encoding.UTF8.WebName,
            NewLine = Environment.NewLine
        });
    }

    public async Task<SaveDocumentResult> SaveAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
    {
        if (document.IsUntitled || string.IsNullOrWhiteSpace(document.FilePath))
        {
            var saveAsResult = await SaveAsAsync(document, cancellationToken);
            return saveAsResult ?? throw new InvalidOperationException("Save was canceled.");
        }

        ValidateExtension(document.FilePath);

#if MACCATALYST
        using var access = CreateSecurityScope(document.FilePath);
#endif

        try
        {
            await File.WriteAllTextAsync(
                document.FilePath,
                NormalizeNewLines(document.Content, document.NewLine),
                ResolveEncoding(document.EncodingName),
                cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Save failed because access was denied. FilePath: {FilePath}", document.FilePath);
            throw;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Save failed because the file is locked or unavailable. FilePath: {FilePath}", document.FilePath);
            throw;
        }

        var fileInfo = new FileInfo(document.FilePath);
        _logger.LogInformation(
            "Saved markdown document. FileName: {FileName}, FilePath: {FilePath}, SizeKB: {SizeKb:F2}, LastModified: {LastModified}",
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length / 1024d,
            fileInfo.LastWriteTimeUtc);

        return new SaveDocumentResult
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    public async Task<SaveDocumentResult?> SaveAsAsync(EditorDocumentState document, CancellationToken cancellationToken = default)
    {
        var suggestedFileName = EnsureValidFileName(document.FileName, allowEmpty: true);
        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            suggestedFileName = "Untitled.mds";
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(suggestedFileName), StringComparer.OrdinalIgnoreCase))
        {
            suggestedFileName = $"{Path.GetFileNameWithoutExtension(suggestedFileName)}.mds";
        }

#if MACCATALYST
        var saveResult = await SaveAsMacCatalystAsync(suggestedFileName, document, cancellationToken);
        return saveResult;
#elif WINDOWS
        var saveResult = await SaveAsWindowsAsync(suggestedFileName, document, cancellationToken);
        return saveResult;
#else
        throw new PlatformNotSupportedException("Save As is only implemented for desktop platforms.");
#endif
    }

    public string? TryCreatePersistentAccessBookmark(string filePath)
    {
#if MACCATALYST
        if (!_securityScopedUrls.TryGetValue(filePath, out var url))
        {
            url = NSUrl.CreateFileUrl(filePath, null);
            if (url is null)
            {
                return null;
            }

            TrackSecurityScopedUrl(filePath, url);
        }

        var bookmarkData = url.CreateBookmarkData(
            NSUrlBookmarkCreationOptions.WithSecurityScope,
            [],
            null,
            out var error);

        if (error is not null || bookmarkData is null)
        {
            _logger.LogWarning("Failed to create persistent access bookmark for file {FilePath}. Error: {Error}", filePath, error?.LocalizedDescription);
            return null;
        }

        return Convert.ToBase64String(bookmarkData.ToArray());
#else
        return null;
#endif
    }

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
#if MACCATALYST
        restoredPath = null;
        isStale = false;

        if (string.IsNullOrWhiteSpace(bookmark))
        {
            return false;
        }

        try
        {
            var bookmarkBytes = Convert.FromBase64String(bookmark);
            using var bookmarkData = NSData.FromArray(bookmarkBytes);
            var resolvedUrl = NSUrl.FromBookmarkData(
                bookmarkData,
                NSUrlBookmarkResolutionOptions.WithSecurityScope,
                null,
                out isStale,
                out var error);

            if (error is not null || resolvedUrl is null)
            {
                _logger.LogWarning("Failed to restore persistent file access bookmark. Error: {Error}", error?.LocalizedDescription);
                return false;
            }

            restoredPath = resolvedUrl.Path;
            if (string.IsNullOrWhiteSpace(restoredPath))
            {
                return false;
            }

            TrackSecurityScopedUrl(restoredPath, resolvedUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode or resolve persistent file access bookmark.");
            restoredPath = null;
            isStale = false;
            return false;
        }
#else
        restoredPath = null;
        isStale = false;
        return false;
#endif
    }

    private static string EnsureValidFileName(string? fileName, bool allowEmpty)
    {
        var trimmed = (fileName ?? string.Empty).Trim();
        if (allowEmpty && string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("File name cannot be empty.");
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (trimmed.Contains(invalidChar))
            {
                throw new InvalidOperationException("The file name contains invalid characters.");
            }
        }

        return trimmed;
    }

    private static void ValidateExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Markdown files must use the .md or .mds extension.");
        }
    }

    private static Encoding ResolveEncoding(string encodingName)
    {
        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.UTF8;
    }

    private static string DetectNewLine(string content)
    {
        if (content.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        return content.Contains('\r') ? "\r" : "\n";
    }

    private static string NormalizeNewLines(string content, string newLine)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", newLine, StringComparison.Ordinal);
    }

#if MACCATALYST
    private async Task<MarkdownDocument> LoadDocumentFromPickedUrlAsync(NSUrl pickedUrl)
    {
        var fullPath = pickedUrl.Path ?? string.Empty;
        TrackSecurityScopedUrl(fullPath, pickedUrl);
        return await LoadDocumentAsync(fullPath);
    }

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
        await File.WriteAllTextAsync(tempPath, NormalizeNewLines(document.Content, document.NewLine), ResolveEncoding(document.EncodingName), cancellationToken);

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
        TrackSecurityScopedUrl(savedPath, pickedUrl);

        var fileInfo = new FileInfo(savedPath);
        return new SaveDocumentResult
        {
            FilePath = savedPath,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : Encoding.UTF8.GetByteCount(document.Content),
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTimeOffset.UtcNow
        };
    }

    private IDisposable? CreateSecurityScope(string filePath)
    {
        if (!_securityScopedUrls.TryGetValue(filePath, out var url))
        {
            return null;
        }

        return new SecurityScopedResourceAccess(url);
    }

    private void TrackSecurityScopedUrl(string filePath, NSUrl url)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _securityScopedUrls[filePath] = url;
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

        await FileIO.WriteTextAsync(file, NormalizeNewLines(document.Content, document.NewLine));
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
