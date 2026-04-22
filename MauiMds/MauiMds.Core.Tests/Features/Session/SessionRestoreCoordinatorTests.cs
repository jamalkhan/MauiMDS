using MauiMds.Features.Session;
using MauiMds.Models;
using MauiMds.Core.Tests.TestHelpers;

namespace MauiMds.Core.Tests.Features.Session;

[TestClass]
public sealed class SessionRestoreCoordinatorTests
{
    private static SessionRestoreCoordinator CreateCoordinator(
        FakeWorkspaceBrowserService? workspaceService = null,
        FakeMarkdownDocumentService? documentService = null,
        FakeSessionStateService? sessionStateService = null,
        FakePlatformInfo? platformInfo = null) =>
        new(
            workspaceService ?? new FakeWorkspaceBrowserService(),
            documentService ?? new FakeMarkdownDocumentService(),
            sessionStateService ?? new FakeSessionStateService(),
            new TestLogger<SessionRestoreCoordinator>(),
            platformInfo ?? new FakePlatformInfo { IsMacCatalyst = false });

    [TestMethod]
    public void Save_PersistsSessionStateAndBookmarks()
    {
        var workspaceService = new FakeWorkspaceBrowserService { BookmarkToReturn = "workspace-bookmark" };
        var documentService = new FakeMarkdownDocumentService { BookmarkToReturn = "document-bookmark" };
        var sessionStateService = new FakeSessionStateService();
        var coordinator = CreateCoordinator(workspaceService, documentService, sessionStateService);

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
        var coordinator = CreateCoordinator(sessionStateService: new FakeSessionStateService { LoadedState = expected });

        var actual = coordinator.Load();

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_WithoutWorkspaceAccess_ReturnsNullForPlainSavedPath()
    {
        var coordinator = CreateCoordinator();

        var path = coordinator.ResolveDocumentRestorePath(new SessionState
        {
            DocumentFilePath = "/tmp/file.mds"
        }, hasWorkspaceAccess: false, out var needsRepick);

        Assert.IsFalse(needsRepick);
        Assert.AreEqual("/tmp/file.mds", path);
    }

    // ── New tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveDocumentRestorePath_NullDocumentFilePath_ReturnsNull()
    {
        var coordinator = CreateCoordinator();

        var path = coordinator.ResolveDocumentRestorePath(new SessionState
        {
            DocumentFilePath = null
        }, hasWorkspaceAccess: false, out var needsRepick);

        Assert.IsFalse(needsRepick);
        Assert.IsNull(path);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_EmptyDocumentFilePath_ReturnsEmpty()
    {
        var coordinator = CreateCoordinator();

        var path = coordinator.ResolveDocumentRestorePath(new SessionState
        {
            DocumentFilePath = string.Empty
        }, hasWorkspaceAccess: false, out var needsRepick);

        Assert.IsFalse(needsRepick);
        Assert.AreEqual(string.Empty, path);
    }

    [TestMethod]
    public void Save_WithNullWorkspacePath_StoresNullBookmark()
    {
        var workspaceService = new FakeWorkspaceBrowserService { BookmarkToReturn = "should-not-be-used" };
        var sessionStateService = new FakeSessionStateService();
        var coordinator = CreateCoordinator(workspaceService, sessionStateService: sessionStateService);

        coordinator.Save(new SessionPersistenceRequest
        {
            WorkspaceRootPath = null,
            DocumentFilePath = null,
            ViewMode = EditorViewMode.TextEditor
        });

        Assert.IsNotNull(sessionStateService.SavedState);
        Assert.IsNull(sessionStateService.SavedState.WorkspaceRootBookmark);
        Assert.IsNull(sessionStateService.SavedState.DocumentFileBookmark);
    }

    [TestMethod]
    public void Save_PreservesViewModeAndPanelState()
    {
        var sessionStateService = new FakeSessionStateService();
        var coordinator = CreateCoordinator(sessionStateService: sessionStateService);

        coordinator.Save(new SessionPersistenceRequest
        {
            WorkspaceRootPath = null,
            DocumentFilePath = null,
            ViewMode = EditorViewMode.Viewer,
            IsWorkspacePanelVisible = false,
            WorkspacePanelWidth = 300
        });

        Assert.AreEqual(EditorViewMode.Viewer, sessionStateService.SavedState!.LastViewMode);
        Assert.IsFalse(sessionStateService.SavedState.IsWorkspacePanelVisible);
        Assert.AreEqual(300, sessionStateService.SavedState.WorkspacePanelWidth);
    }

    // ── Mac Catalyst path tests ──────────────────────────────────────────────

    [TestMethod]
    public void ResolveWorkspaceRestorePath_MacCatalyst_WithValidBookmark_ReturnsRestoredPath()
    {
        var workspaceService = new FakeWorkspaceBrowserService
        {
            RestoreBookmarkResult = true,
            RestoredPath = "/restored/workspace"
        };
        var coordinator = CreateCoordinator(
            workspaceService: workspaceService,
            platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveWorkspaceRestorePath(
            new SessionState { WorkspaceRootBookmark = "some-bookmark" },
            out var repickMessage);

        Assert.AreEqual("/restored/workspace", path);
        Assert.IsNull(repickMessage);
    }

    [TestMethod]
    public void ResolveWorkspaceRestorePath_MacCatalyst_WithStaleBookmark_ReturnsRestoredPathAndLogs()
    {
        var workspaceService = new FakeWorkspaceBrowserService
        {
            RestoreBookmarkResult = true,
            RestoredPath = "/restored/workspace",
            RestoredBookmarkIsStale = true
        };
        var coordinator = CreateCoordinator(
            workspaceService: workspaceService,
            platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveWorkspaceRestorePath(
            new SessionState { WorkspaceRootBookmark = "stale-bookmark" },
            out var repickMessage);

        Assert.AreEqual("/restored/workspace", path);
        Assert.IsNull(repickMessage);
    }

    [TestMethod]
    public void ResolveWorkspaceRestorePath_MacCatalyst_WithFailedBookmark_ReturnsNullWithRepickMessage()
    {
        var workspaceService = new FakeWorkspaceBrowserService
        {
            RestoreBookmarkResult = false,
            RestoredPath = null
        };
        var coordinator = CreateCoordinator(
            workspaceService: workspaceService,
            platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveWorkspaceRestorePath(
            new SessionState { WorkspaceRootBookmark = "bad-bookmark" },
            out var repickMessage);

        Assert.IsNull(path);
        Assert.IsNotNull(repickMessage);
        StringAssert.Contains(repickMessage, "Open Folder");
    }

    [TestMethod]
    public void ResolveWorkspaceRestorePath_MacCatalyst_NoBookmarkButHasPath_ReturnsNullWithRepickMessage()
    {
        var coordinator = CreateCoordinator(platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveWorkspaceRestorePath(
            new SessionState { WorkspaceRootPath = "/old/workspace", WorkspaceRootBookmark = null },
            out var repickMessage);

        Assert.IsNull(path);
        Assert.IsNotNull(repickMessage);
    }

    [TestMethod]
    public void ResolveWorkspaceRestorePath_MacCatalyst_NothingSaved_ReturnsNullNoRepickMessage()
    {
        var coordinator = CreateCoordinator(platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveWorkspaceRestorePath(new SessionState(), out var repickMessage);

        Assert.IsNull(path);
        Assert.IsNull(repickMessage);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_MacCatalyst_WithValidBookmark_ReturnsRestoredPath()
    {
        var documentService = new FakeMarkdownDocumentService
        {
            RestoreBookmarkResult = true,
            RestoredPath = "/restored/file.mds"
        };
        var coordinator = CreateCoordinator(
            documentService: documentService,
            platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveDocumentRestorePath(
            new SessionState { DocumentFileBookmark = "doc-bookmark" },
            hasWorkspaceAccess: false,
            out var needsRepick);

        Assert.AreEqual("/restored/file.mds", path);
        Assert.IsFalse(needsRepick);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_MacCatalyst_WithFailedBookmark_SetsNeedsRepick()
    {
        var documentService = new FakeMarkdownDocumentService
        {
            RestoreBookmarkResult = false,
            RestoredPath = null
        };
        var coordinator = CreateCoordinator(
            documentService: documentService,
            platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveDocumentRestorePath(
            new SessionState { DocumentFileBookmark = "bad-doc-bookmark" },
            hasWorkspaceAccess: false,
            out var needsRepick);

        Assert.IsNull(path);
        Assert.IsTrue(needsRepick);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_MacCatalyst_NoBookmark_HasWorkspaceAccess_ReturnsDocumentPath()
    {
        var coordinator = CreateCoordinator(platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveDocumentRestorePath(
            new SessionState { DocumentFilePath = "/workspace/doc.mds" },
            hasWorkspaceAccess: true,
            out var needsRepick);

        Assert.AreEqual("/workspace/doc.mds", path);
        Assert.IsFalse(needsRepick);
    }

    [TestMethod]
    public void ResolveDocumentRestorePath_MacCatalyst_NoBookmark_NoWorkspaceAccess_ReturnsNull()
    {
        var coordinator = CreateCoordinator(platformInfo: new FakePlatformInfo { IsMacCatalyst = true });

        var path = coordinator.ResolveDocumentRestorePath(
            new SessionState { DocumentFilePath = "/workspace/doc.mds" },
            hasWorkspaceAccess: false,
            out var needsRepick);

        Assert.IsNull(path);
        Assert.IsFalse(needsRepick);
    }
}
