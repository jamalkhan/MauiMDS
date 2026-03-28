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
    private static readonly string[] AllowedExtensions = [".md", ".mds"];
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

    private static List<WorkspaceNodeInfo> BuildChildren(DirectoryInfo directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = directory
            .EnumerateDirectories()
            .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(child => new WorkspaceNodeInfo
            {
                FullPath = child.FullName,
                IsDirectory = true,
                Children = BuildChildren(child, cancellationToken)
            });

        var files = directory
            .EnumerateFiles()
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
