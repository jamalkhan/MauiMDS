using MauiMds.Models;
using Microsoft.Extensions.Logging;
#if MACCATALYST
using Foundation;
using UniformTypeIdentifiers;
using UIKit;
#endif

namespace MauiMds.Services;

public sealed class WorkspaceBrowserService : IWorkspaceBrowserService
{
    private static readonly string[] AllowedExtensions = [".md", ".mds", ".m4a"];
    private readonly ILogger<WorkspaceBrowserService> _logger;
#if MACCATALYST
    private SecurityScopedResourceAccess? _currentWorkspaceAccess;
#endif

    public WorkspaceBrowserService(ILogger<WorkspaceBrowserService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> PickFolderAsync()
    {
#if MACCATALYST
        var picker = new UIDocumentPickerViewController([CreateContentType("public.folder")], asCopy: false)
        {
            AllowsMultipleSelection = false
        };

        var pickedUrl = await PresentPickerAsync(picker);
        if (pickedUrl is null)
        {
            return null;
        }

        _currentWorkspaceAccess?.Dispose();
        _currentWorkspaceAccess = new SecurityScopedResourceAccess(pickedUrl);
        _logger.LogInformation(
            "Picked workspace folder. FolderPath: {FolderPath}, AccessGranted: {AccessGranted}",
            pickedUrl.Path,
            _currentWorkspaceAccess.HasAccess);
        return pickedUrl.Path;
#else
        var result = await FolderPicker.Default.PickAsync(default);
        return result.IsSuccessful ? result.Folder.Path : null;
#endif
    }

    public Task<IReadOnlyList<WorkspaceNodeInfo>> LoadWorkspaceTreeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<WorkspaceNodeInfo>>(() =>
        {
            var rootDirectory = new DirectoryInfo(rootPath);
            if (!rootDirectory.Exists)
            {
                return [];
            }

            var children = BuildChildren(rootDirectory, cancellationToken);
            return children;
        }, cancellationToken);
    }

    public async Task<MarkdownDocument> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Please choose a .mds or .md file.");
        }

        var fileInfo = new FileInfo(filePath);
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        return new MarkdownDocument
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Content = content
        };
    }

    public async Task<bool> FileContainsTextAsync(string filePath, string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return content.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> CreateMarkdownSharpFileAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(directoryPath);

        var index = 0;
        string fileName;
        string fullPath;

        do
        {
            fileName = index == 0 ? "New_Markdown_Sharp.mds" : $"New_Markdown_Sharp_{index}.mds";
            fullPath = Path.Combine(directoryPath, fileName);
            index++;
        }
        while (File.Exists(fullPath));

        await File.WriteAllTextAsync(fullPath, string.Empty, cancellationToken);
        _logger.LogInformation("Created markdown file {FilePath}", fullPath);
        return fullPath;
    }

    public Task<string> RenameMarkdownFileAsync(string filePath, string newFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sanitizedFileName = newFileName.Trim();
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            throw new InvalidOperationException("File name cannot be empty.");
        }

        var extension = Path.GetExtension(sanitizedFileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Markdown files must keep a .md or .mds extension.");
        }

        var directoryPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Unable to determine the file directory.");
        var targetPath = Path.Combine(directoryPath, sanitizedFileName);

        if (string.Equals(filePath, targetPath, StringComparison.Ordinal))
        {
            return Task.FromResult(filePath);
        }

        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException("A file with that name already exists.");
        }

        File.Move(filePath, targetPath);
        _logger.LogInformation("Renamed markdown file from {OldPath} to {NewPath}", filePath, targetPath);
        return Task.FromResult(targetPath);
    }

    public string? TryCreatePersistentAccessBookmark(string folderPath)
    {
#if MACCATALYST
        if (_currentWorkspaceAccess?.Url is null || !string.Equals(_currentWorkspaceAccess.Url.Path, folderPath, StringComparison.Ordinal))
        {
            return null;
        }

        var bookmarkData = _currentWorkspaceAccess.Url.CreateBookmarkData(
            NSUrlBookmarkCreationOptions.WithSecurityScope,
            [],
            null,
            out var error);

        if (error is not null || bookmarkData is null)
        {
            _logger.LogWarning("Failed to create persistent access bookmark for workspace {FolderPath}. Error: {Error}", folderPath, error?.LocalizedDescription);
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
                _logger.LogWarning("Failed to restore persistent workspace access bookmark. Error: {Error}", error?.LocalizedDescription);
                return false;
            }

            restoredPath = resolvedUrl.Path;
            if (string.IsNullOrWhiteSpace(restoredPath))
            {
                return false;
            }

            _currentWorkspaceAccess?.Dispose();
            _currentWorkspaceAccess = new SecurityScopedResourceAccess(resolvedUrl);
            if (!_currentWorkspaceAccess.HasAccess || !CanEnumerateWorkspaceRoot(restoredPath))
            {
                _logger.LogWarning(
                    "Workspace bookmark resolved but access could not be validated. WorkspaceRootPath: {WorkspaceRootPath}, IsStale: {IsStale}, AccessGranted: {AccessGranted}",
                    restoredPath,
                    isStale,
                    _currentWorkspaceAccess.HasAccess);
                _currentWorkspaceAccess.Dispose();
                _currentWorkspaceAccess = null;
                restoredPath = null;
                return false;
            }

            _logger.LogInformation(
                "Restored workspace bookmark. WorkspaceRootPath: {WorkspaceRootPath}, IsStale: {IsStale}, AccessGranted: {AccessGranted}",
                restoredPath,
                isStale,
                _currentWorkspaceAccess.HasAccess);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode or resolve persistent workspace access bookmark.");
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

    private bool CanEnumerateWorkspaceRoot(string rootPath)
    {
        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(rootPath).GetEnumerator();
            _ = enumerator.MoveNext();
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Workspace access validation failed for {WorkspaceRootPath}.", rootPath);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Workspace access validation could not enumerate {WorkspaceRootPath}.", rootPath);
            return false;
        }
    }

    private List<WorkspaceNodeInfo> BuildChildren(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = EnumerateDirectoriesSafely(directory)
            .Where(child => !IsHidden(child))
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(child =>
            {
                var children = BuildChildren(child, cancellationToken);
                return new WorkspaceNodeInfo
                {
                    FullPath = child.FullName,
                    IsDirectory = true,
                    Children = children
                };
            });

        var files = EnumerateFilesSafely(directory)
            .Where(file => !IsHidden(file))
            .Where(file => AllowedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new WorkspaceNodeInfo
            {
                FullPath = file.FullName,
                IsDirectory = false,
                Children = []
            });

        return directories.Concat(files).ToList();
    }

    private IEnumerable<DirectoryInfo> EnumerateDirectoriesSafely(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Skipping unreadable workspace directory {DirectoryPath}", directory.FullName);
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Skipping workspace directory {DirectoryPath} because it could not be enumerated.", directory.FullName);
            return [];
        }
    }

    private IEnumerable<FileInfo> EnumerateFilesSafely(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Skipping files in unreadable workspace directory {DirectoryPath}", directory.FullName);
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Skipping files in workspace directory {DirectoryPath} because they could not be enumerated.", directory.FullName);
            return [];
        }
    }

    private static bool IsHidden(FileSystemInfo fileSystemInfo)
    {
        if (fileSystemInfo.Name.StartsWith(".", StringComparison.Ordinal))
        {
            return true;
        }

        var attributes = fileSystemInfo.Attributes;
        return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
    }

#if MACCATALYST
    private static Task<NSUrl?> PresentPickerAsync(UIDocumentPickerViewController picker)
    {
        var tcs = new TaskCompletionSource<NSUrl?>();
        var pickerDelegate = new WorkspaceFolderPickerDelegate(tcs);
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

    private sealed class WorkspaceFolderPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly TaskCompletionSource<NSUrl?> _tcs;

        public WorkspaceFolderPickerDelegate(TaskCompletionSource<NSUrl?> tcs)
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
        public NSUrl Url { get; }
        private readonly NSUrl _url;
        public bool HasAccess { get; }

        public SecurityScopedResourceAccess(NSUrl url)
        {
            Url = url;
            _url = url;
            HasAccess = _url.StartAccessingSecurityScopedResource();
        }

        public void Dispose()
        {
            if (HasAccess)
            {
                _url.StopAccessingSecurityScopedResource();
            }
        }
    }
#endif
}
