using MauiMds.Features.Editor;
using MauiMds.Models;
using MauiMds.Processors;
using MauiMds.Core.Tests.TestHelpers;

namespace MauiMds.Core.Tests.Features.Editor;

file sealed class ThrowingMdsParser : MdsParser
{
    public ThrowingMdsParser() : base(new TestLogger<MdsParser>()) { }
    public override List<MarkdownBlock> Parse(string content) => throw new InvalidOperationException("Parse failure");
}

[TestClass]
public sealed class DocumentWorkflowServiceTests
{
    private static DocumentWorkflowService CreateController() =>
        new(new MdsParser(new TestLogger<MdsParser>()), new TestLogger<DocumentWorkflowService>());

    [TestMethod]
    public void PrepareDocument_ParsesMarkdownAndPreservesViewerMode()
    {
        var result = CreateController().PrepareDocument(new MarkdownDocument
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
        var document = new EditorDocumentState
        {
            FilePath = string.Empty,
            FileName = "Untitled.mds",
            Content = "abc",
            OriginalContent = string.Empty,
            IsUntitled = true,
            IsDirty = true
        };

        CreateController().ApplySaveResult(document, new SaveDocumentResult
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

    // ── New tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public void PrepareDocument_EmptyContent_ReturnsZeroBlocks()
    {
        var result = CreateController().PrepareDocument(new MarkdownDocument
        {
            FilePath = "/tmp/empty.mds",
            FileName = "empty.mds",
            Content = string.Empty
        }, EditorViewMode.TextEditor);

        Assert.AreEqual(0, result.Blocks.Count);
        Assert.IsNull(result.InlineErrorMessage);
    }

    [TestMethod]
    public void PrepareDocument_MultipleBlockTypes_AllParsed()
    {
        var result = CreateController().PrepareDocument(new MarkdownDocument
        {
            FilePath = "/tmp/multi.mds",
            FileName = "multi.mds",
            Content = "# Heading\n\nParagraph\n\n- bullet"
        }, EditorViewMode.Viewer);

        Assert.IsTrue(result.Blocks.Any(b => b.Type == BlockType.Header));
        Assert.IsTrue(result.Blocks.Any(b => b.Type == BlockType.Paragraph));
        Assert.IsTrue(result.Blocks.Any(b => b.Type == BlockType.BulletListItem));
    }

    [TestMethod]
    public void PrepareDocument_TextEditorMode_ViewModePreserved()
    {
        var result = CreateController().PrepareDocument(new MarkdownDocument
        {
            FilePath = "/tmp/doc.mds",
            FileName = "doc.mds",
            Content = "# Hello"
        }, EditorViewMode.TextEditor);

        Assert.AreEqual(EditorViewMode.TextEditor, result.ViewMode);
    }

    [TestMethod]
    public void PrepareDocument_SetsDocumentStateContent()
    {
        const string content = "# Title\n\nBody text";
        var result = CreateController().PrepareDocument(new MarkdownDocument
        {
            FilePath = "/tmp/doc.mds",
            FileName = "doc.mds",
            Content = content
        }, EditorViewMode.Viewer);

        Assert.AreEqual(content, result.DocumentState.Content);
        Assert.AreEqual(content, result.DocumentState.OriginalContent);
    }

    [TestMethod]
    public void ApplySaveResult_UntitledBecomesNamedFile_ClearsIsUntitled()
    {
        var document = new EditorDocumentState
        {
            FilePath = string.Empty,
            FileName = "Untitled.mds",
            Content = "draft",
            OriginalContent = "draft",
            IsUntitled = true,
            IsDirty = false
        };

        CreateController().ApplySaveResult(document, new SaveDocumentResult
        {
            FilePath = "/docs/notes.mds",
            FileName = "notes.mds",
            FileSizeBytes = 5,
            LastModified = DateTimeOffset.UtcNow
        });

        Assert.IsFalse(document.IsUntitled);
        Assert.AreEqual("/docs/notes.mds", document.FilePath);
        Assert.AreEqual("notes.mds", document.FileName);
    }

    [TestMethod]
    public void PreparePreview_WhenParserThrows_ReturnsFallbackParagraph()
    {
        var controller = new DocumentWorkflowService(
            new ThrowingMdsParser(),
            new TestLogger<DocumentWorkflowService>());

        var result = controller.PreparePreview(new MarkdownDocument
        {
            FilePath = "/tmp/broken.mds",
            FileName = "broken.mds",
            Content = "some content"
        }, EditorViewMode.Viewer);

        Assert.AreEqual(1, result.Blocks.Count);
        Assert.AreEqual(BlockType.Paragraph, result.Blocks[0].Type);
        Assert.AreEqual("some content", result.Blocks[0].Content);
        Assert.IsNotNull(result.InlineErrorMessage);
    }

    [TestMethod]
    public void PreparePreview_WhenParserThrows_RichTextEditorDowngradesToTextEditor()
    {
        var controller = new DocumentWorkflowService(
            new ThrowingMdsParser(),
            new TestLogger<DocumentWorkflowService>());

        var result = controller.PreparePreview(new MarkdownDocument
        {
            FilePath = "/tmp/broken.mds",
            FileName = "broken.mds",
            Content = "some content"
        }, EditorViewMode.RichTextEditor);

        Assert.AreEqual(EditorViewMode.TextEditor, result.ViewMode);
    }

    [TestMethod]
    public void ApplySaveResult_SetsOriginalContentToCurrentContent()
    {
        var document = new EditorDocumentState
        {
            FilePath = "/tmp/existing.mds",
            FileName = "existing.mds",
            Content = "updated content",
            OriginalContent = "old content",
            IsUntitled = false,
            IsDirty = true
        };

        CreateController().ApplySaveResult(document, new SaveDocumentResult
        {
            FilePath = "/tmp/existing.mds",
            FileName = "existing.mds",
            FileSizeBytes = 15,
            LastModified = DateTimeOffset.UtcNow
        });

        Assert.AreEqual("updated content", document.OriginalContent);
        Assert.IsFalse(document.IsDirty);
    }
}
