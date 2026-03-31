using MauiMds.Features.Session;
using MauiMds.Models;
using MauiMds.Tests.TestHelpers;

namespace MauiMds.Tests.Features.Session;

[TestClass]
public sealed class SessionRestoreCoordinatorTests
{
    [TestMethod]
    public void Save_PersistsSessionStateAndBookmarks()
    {
        var workspaceService = new FakeWorkspaceBrowserService { BookmarkToReturn = "workspace-bookmark" };
        var documentService = new FakeMarkdownDocumentService { BookmarkToReturn = "document-bookmark" };
        var sessionStateService = new FakeSessionStateService();
        var coordinator = new SessionRestoreCoordinator(
            workspaceService,
            documentService,
            sessionStateService,
            new TestLogger<SessionRestoreCoordinator>());

        coordinator.Save(new SessionPersistenceRequest
        {
            WorkspaceRootPath = "/workspace",
            DocumentFilePath = "/workspace/file.mds",
            CurrentFolderPath = "/workspace",
            ViewMode = EditorViewMode.TextEditor,
            IsWorkspacePanelVisible = true,
            WorkspacePanelWidth = 240
        });

        Assert.IsNotNull(sessionStateService.SavedState);
        Assert.AreEqual("/workspace", sessionStateService.SavedState.WorkspaceRootPath);
        Assert.AreEqual("workspace-bookmark", sessionStateService.SavedState.WorkspaceRootBookmark);
        Assert.AreEqual("/workspace/file.mds", sessionStateService.SavedState.DocumentFilePath);
        Assert.AreEqual("document-bookmark", sessionStateService.SavedState.DocumentFileBookmark);
        Assert.AreEqual(EditorViewMode.TextEditor, sessionStateService.SavedState.LastViewMode);
        Assert.IsTrue(sessionStateService.SavedState.IsWorkspacePanelVisible);
        Assert.AreEqual(240, sessionStateService.SavedState.WorkspacePanelWidth);
    }

    [TestMethod]
    public void Load_ReturnsStateFromBackingStore()
    {
        var expected = new SessionState { DocumentFilePath = "/tmp/example.mds" };
        var coordinator = new SessionRestoreCoordinator(
            new FakeWorkspaceBrowserService(),
            new FakeMarkdownDocumentService(),
            new FakeSessionStateService { LoadedState = expected },
            new TestLogger<SessionRestoreCoordinator>());

        var actual = coordinator.Load();

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_WithoutWorkspaceAccess_ReturnsNullForPlainSavedPath()
    {
        var coordinator = new SessionRestoreCoordinator(
            new FakeWorkspaceBrowserService(),
            new FakeMarkdownDocumentService(),
            new FakeSessionStateService(),
            new TestLogger<SessionRestoreCoordinator>());

        var path = coordinator.ResolveDocumentRestorePath(new SessionState
        {
            DocumentFilePath = "/tmp/file.mds"
        }, hasWorkspaceAccess: false, out var needsRepick);

        Assert.IsFalse(needsRepick);
        Assert.AreEqual("/tmp/file.mds", path);
    }
}
