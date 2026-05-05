using MauiMds.Models;
using MauiMds.Processors;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Editor;

public sealed class DocumentWorkflowController
{
    private readonly MdsParser _parser;
    private readonly ILogger<DocumentWorkflowController> _logger;

    public DocumentWorkflowController(MdsParser parser, ILogger<DocumentWorkflowController> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    public EditorDocumentState CreateDocumentState(MarkdownDocument document)
    {
        return new EditorDocumentState
        {
            FilePath = document.IsUntitled ? string.Empty : document.FilePath,
            FileName = document.FileName ?? Path.GetFileName(document.FilePath),
            Content = document.Content,
            OriginalContent = document.Content,
            IsUntitled = document.IsUntitled || !Path.IsPathRooted(document.FilePath),
            IsDirty = false,
            FileSizeBytes = document.FileSizeBytes,
            LastModified = document.LastModified,
            EncodingName = document.EncodingName,
            NewLine = document.NewLine
        };
    }

    public DocumentPreviewResult PreparePreview(MarkdownDocument document, EditorViewMode currentViewMode)
    {
        try
        {
            var blocks = _parser.Parse(document.Content);
            return new DocumentPreviewResult
            {
                Blocks = blocks,
                ViewMode = currentViewMode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Markdown parsing failed. Falling back to plaintext paragraph rendering.");
            return new DocumentPreviewResult
            {
                Blocks = [new MarkdownBlock { Type = BlockType.Paragraph, Content = document.Content }],
                ViewMode = currentViewMode == EditorViewMode.RichTextEditor ? EditorViewMode.TextEditor : currentViewMode,
                InlineErrorMessage = "Markdown parsing failed. The document is shown in a safe fallback mode."
            };
        }
    }

    public DocumentLoadResult PrepareDocument(MarkdownDocument document, EditorViewMode currentViewMode)
    {
        var preview = PreparePreview(document, currentViewMode);
        return new DocumentLoadResult
        {
            DocumentState = CreateDocumentState(document),
            Blocks = preview.Blocks,
            ViewMode = preview.ViewMode,
            InlineErrorMessage = preview.InlineErrorMessage
        };
    }

    public void ApplySaveResult(EditorDocumentState document, SaveDocumentResult result)
    {
        document.FilePath = result.FilePath;
        document.FileName = result.FileName;
        document.IsUntitled = false;
        document.IsDirty = false;
        document.OriginalContent = document.Content;
        document.FileSizeBytes = result.FileSizeBytes;
        document.LastModified = result.LastModified;
    }
}
