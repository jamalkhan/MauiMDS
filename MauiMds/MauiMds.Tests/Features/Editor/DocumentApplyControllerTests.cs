using MauiMds.Features.Editor;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Tests.TestHelpers;

namespace MauiMds.Tests.Features.Editor;

[TestClass]
public sealed class DocumentApplyControllerTests
{
    [TestMethod]
    public void PrepareApply_FlagsChangedDocumentMetadataAndWatchState()
    {
        var controller = CreateController();

        var result = controller.PrepareApply(
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
        var controller = CreateController();

        var result = controller.PrepareApply(
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

    private static DocumentApplyController CreateController()
    {
        return new DocumentApplyController(
            new DocumentWorkflowController(
                new MdsParser(new TestLogger<MdsParser>()),
                new TestLogger<DocumentWorkflowController>()));
    }
}
