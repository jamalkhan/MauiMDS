using MauiMds.Features.Editor;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Tests.TestHelpers;

namespace MauiMds.Tests.Features.Editor;

[TestClass]
public sealed class DocumentWorkflowControllerTests
{
    [TestMethod]
    public void PrepareDocument_ParsesMarkdownAndPreservesViewerMode()
    {
        var controller = new DocumentWorkflowController(
            new MdsParser(new TestLogger<MdsParser>()),
            new TestLogger<DocumentWorkflowController>());

        var result = controller.PrepareDocument(new MarkdownDocument
        {
            FilePath = "/tmp/example.mds",
            FileName = "example.mds",
            Content = "# Hello"
        }, EditorViewMode.Viewer);

        Assert.AreEqual(EditorViewMode.Viewer, result.ViewMode);
        Assert.AreEqual("example.mds", result.DocumentState.FileName);
        Assert.AreEqual(BlockType.Header, result.Blocks.Single().Type);
        Assert.IsNull(result.InlineErrorMessage);
    }

    [TestMethod]
    public void ApplySaveResult_UpdatesDocumentStateFromSaveResult()
    {
        var controller = new DocumentWorkflowController(
            new MdsParser(new TestLogger<MdsParser>()),
            new TestLogger<DocumentWorkflowController>());

        var document = new EditorDocumentState
        {
            FilePath = string.Empty,
            FileName = "Untitled.mds",
            Content = "abc",
            OriginalContent = string.Empty,
            IsUntitled = true,
            IsDirty = true
        };

        controller.ApplySaveResult(document, new SaveDocumentResult
        {
            FilePath = "/tmp/saved.mds",
            FileName = "saved.mds",
            FileSizeBytes = 3,
            LastModified = DateTimeOffset.UtcNow
        });

        Assert.AreEqual("/tmp/saved.mds", document.FilePath);
        Assert.AreEqual("saved.mds", document.FileName);
        Assert.IsFalse(document.IsUntitled);
        Assert.IsFalse(document.IsDirty);
        Assert.AreEqual("abc", document.OriginalContent);
        Assert.AreEqual(3, document.FileSizeBytes);
        Assert.IsNotNull(document.LastModified);
    }
}
