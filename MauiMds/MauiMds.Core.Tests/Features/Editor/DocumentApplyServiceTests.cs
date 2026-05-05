using MauiMds.Features.Editor;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Core.Tests.TestHelpers;

namespace MauiMds.Core.Tests.Features.Editor;

[TestClass]
public sealed class DocumentApplyControllerTests
{
    private static DocumentApplyController CreateController() =>
        new(new DocumentWorkflowController(
            new MdsParser(new TestLogger<MdsParser>()),
            new TestLogger<DocumentWorkflowController>()));

    [TestMethod]
    public void PrepareApply_FlagsChangedDocumentMetadataAndWatchState()
    {
        var result = CreateController().PrepareApply(
            new EditorDocumentState
            {
                FilePath = string.Empty,
                FileName = "Untitled.mds",
                Content = "draft",
                OriginalContent = "draft",
                IsUntitled = true,
                IsDirty = false
            },
            new MarkdownDocument
            {
                FilePath = "/tmp/notes/example.mds",
                FileName = "example.mds",
                Content = "# Hello",
                IsUntitled = false
            });

        Assert.IsTrue(result.FilePathChanged);
        Assert.IsTrue(result.FileNameChanged);
        Assert.IsFalse(result.IsDirtyChanged);
        Assert.IsTrue(result.IsUntitledChanged);
        Assert.IsTrue(result.ShouldWatchDocument);
        Assert.AreEqual("/tmp/notes/example.mds", result.WatchFilePath);
        Assert.AreEqual("example.mds", result.DocumentState.FileName);
        Assert.IsFalse(result.DocumentState.IsUntitled);
    }

    [TestMethod]
    public void PrepareApply_ForUntitledDocument_DisablesWatchingAndKeepsStableFlags()
    {
        var result = CreateController().PrepareApply(
            new EditorDocumentState
            {
                FilePath = string.Empty,
                FileName = "Untitled.mds",
                Content = string.Empty,
                OriginalContent = string.Empty,
                IsUntitled = true,
                IsDirty = false
            },
            new MarkdownDocument
            {
                FilePath = string.Empty,
                FileName = "Untitled.mds",
                Content = "new draft",
                IsUntitled = true
            });

        Assert.IsFalse(result.FilePathChanged);
        Assert.IsFalse(result.FileNameChanged);
        Assert.IsFalse(result.IsDirtyChanged);
        Assert.IsFalse(result.IsUntitledChanged);
        Assert.IsFalse(result.ShouldWatchDocument);
        Assert.IsNull(result.WatchFilePath);
    }

    // ── New tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public void PrepareApply_SameDocument_NoFieldsChanged()
    {
        var result = CreateController().PrepareApply(
            new EditorDocumentState
            {
                FilePath = "/tmp/notes.mds",
                FileName = "notes.mds",
                Content = "# Hello",
                OriginalContent = "# Hello",
                IsUntitled = false,
                IsDirty = false
            },
            new MarkdownDocument
            {
                FilePath = "/tmp/notes.mds",
                FileName = "notes.mds",
                Content = "# Hello",
                IsUntitled = false
            });

        Assert.IsFalse(result.FilePathChanged);
        Assert.IsFalse(result.FileNameChanged);
        Assert.IsFalse(result.IsDirtyChanged);
        Assert.IsFalse(result.IsUntitledChanged);
    }

    [TestMethod]
    public void PrepareApply_NamedDocument_EnablesWatch()
    {
        var result = CreateController().PrepareApply(
            new EditorDocumentState
            {
                FilePath = string.Empty,
                FileName = "Untitled.mds",
                Content = string.Empty,
                OriginalContent = string.Empty,
                IsUntitled = true,
                IsDirty = false
            },
            new MarkdownDocument
            {
                FilePath = "/workspace/readme.mds",
                FileName = "readme.mds",
                Content = "# Readme",
                IsUntitled = false
            });

        Assert.IsTrue(result.ShouldWatchDocument);
        Assert.AreEqual("/workspace/readme.mds", result.WatchFilePath);
    }

    [TestMethod]
    public void PrepareApply_DocumentStateReflectsIncomingDocument()
    {
        var result = CreateController().PrepareApply(
            new EditorDocumentState
            {
                FilePath = string.Empty,
                FileName = "Untitled.mds",
                Content = string.Empty,
                OriginalContent = string.Empty,
                IsUntitled = true,
                IsDirty = false
            },
            new MarkdownDocument
            {
                FilePath = "/tmp/new.mds",
                FileName = "new.mds",
                Content = "New content",
                IsUntitled = false
            });

        Assert.AreEqual("New content", result.DocumentState.Content);
        Assert.AreEqual("new.mds", result.DocumentState.FileName);
        Assert.IsFalse(result.DocumentState.IsUntitled);
    }
}
