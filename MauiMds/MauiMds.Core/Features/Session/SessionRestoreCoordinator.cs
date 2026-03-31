using MauiMds.Models;
using MauiMds.Services;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Session;

public sealed class SessionRestoreCoordinator
{
    private readonly IWorkspaceBrowserService _workspaceBrowserService;
    private readonly IMarkdownDocumentService _documentService;
    private readonly ISessionStateService _sessionStateService;
    private readonly ILogger<SessionRestoreCoordinator> _logger;

    public SessionRestoreCoordinator(
        IWorkspaceBrowserService workspaceBrowserService,
        IMarkdownDocumentService documentService,
        ISessionStateService sessionStateService,
        ILogger<SessionRestoreCoordinator> logger)
    {
        _workspaceBrowserService = workspaceBrowserService;
        _documentService = documentService;
        _sessionStateService = sessionStateService;
        _logger = logger;
    }

    public SessionState Load() => _sessionStateService.Load();

    public void Save(SessionPersistenceRequest request)
    {
        var sessionState = new SessionState
        {
            WorkspaceRootPath = request.WorkspaceRootPath,
            WorkspaceRootBookmark = string.IsNullOrWhiteSpace(request.WorkspaceRootPath) ? null : _workspaceBrowserService.TryCreatePersistentAccessBookmark(request.WorkspaceRootPath),
            DocumentFilePath = request.DocumentFilePath,
            DocumentFileBookmark = string.IsNullOrWhiteSpace(request.DocumentFilePath) ? null : _documentService.TryCreatePersistentAccessBookmark(request.DocumentFilePath),
            CurrentFolderPath = request.CurrentFolderPath,
            LastViewMode = request.ViewMode,
            IsWorkspacePanelVisible = request.IsWorkspacePanelVisible,
            WorkspacePanelWidth = request.WorkspacePanelWidth
        };

        _sessionStateService.Save(sessionState);
    }

    public string? ResolveWorkspaceRestorePath(SessionState sessionState, out string? repickMessage)
    {
        repickMessage = null;

        if (OperatingSystem.IsMacCatalyst())
        {
            if (!string.IsNullOrWhiteSpace(sessionState.WorkspaceRootBookmark))
            {
                if (_workspaceBrowserService.TryRestorePersistentAccessFromBookmark(sessionState.WorkspaceRootBookmark, out var restoredPath, out var isStale) &&
                    !string.IsNullOrWhiteSpace(restoredPath))
                {
                    if (isStale)
                    {
                        _logger.LogWarning("Workspace bookmark resolved but is stale. WorkspaceRootPath: {WorkspaceRootPath}", restoredPath);
                    }

                    return restoredPath;
                }

                repickMessage = "The previous workspace folder needs permission again. Please use Open Folder to re-pick it.";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(sessionState.WorkspaceRootPath))
            {
                repickMessage = "The previous workspace folder needs permission again. Please use Open Folder to re-pick it.";
            }

            return null;
        }

        return sessionState.WorkspaceRootPath;
    }

    public string? ResolveDocumentRestorePath(SessionState sessionState, bool hasWorkspaceAccess, out bool needsRepick)
    {
        needsRepick = false;

        if (OperatingSystem.IsMacCatalyst())
        {
            if (!string.IsNullOrWhiteSpace(sessionState.DocumentFileBookmark))
            {
                if (_documentService.TryRestorePersistentAccessFromBookmark(sessionState.DocumentFileBookmark, out var restoredPath, out var isStale) &&
                    !string.IsNullOrWhiteSpace(restoredPath))
                {
                    if (isStale)
                    {
                        _logger.LogWarning("Document bookmark resolved but is stale. DocumentFilePath: {DocumentFilePath}", restoredPath);
                    }

                    return restoredPath;
                }

                needsRepick = true;
                return null;
            }

            return hasWorkspaceAccess ? sessionState.DocumentFilePath : null;
        }

        return sessionState.DocumentFilePath;
    }
}
