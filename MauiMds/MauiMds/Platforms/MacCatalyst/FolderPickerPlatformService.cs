using Foundation;
using MauiMds.Services;
using UniformTypeIdentifiers;
using UIKit;
using Microsoft.Extensions.Logging;

namespace MauiMds.Services;

internal sealed class FolderPickerPlatformService(ILogger<FolderPickerPlatformService> logger) : IFolderPickerPlatformService
{
    private readonly ILogger<FolderPickerPlatformService> _logger = logger;
    private SecurityScopedResourceAccess? _currentWorkspaceAccess;

    public async Task<string?> PickFolderAsync()
    {
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
    }

    public string? TryCreatePersistentAccessBookmark(string folderPath)
    {
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
    }

    public bool TryRestorePersistentAccessFromBookmark(string bookmark, out string? restoredPath, out bool isStale)
    {
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

    private static Task<NSUrl?> PresentPickerAsync(UIDocumentPickerViewController picker)
    {
        var tcs = new TaskCompletionSource<NSUrl?>();
        picker.Delegate = new WorkspaceFolderPickerDelegate(tcs);
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
        return UTType.CreateFromIdentifier(identifier)
            ?? throw new InvalidOperationException($"Unable to create content type for '{identifier}'.");
    }

    private sealed class WorkspaceFolderPickerDelegate(TaskCompletionSource<NSUrl?> tcs) : UIDocumentPickerDelegate
    {
        public override void WasCancelled(UIDocumentPickerViewController controller)
            => tcs.TrySetResult(null);

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
            => tcs.TrySetResult(urls.FirstOrDefault());
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
}
