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

    public DocumentLoadResult PrepareDocument(MarkdownDocument document, EditorViewMode currentViewMode)
    {
        var nextState = new EditorDocumentState
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

        try
        {
            var blocks = _parser.Parse(document.Content);
            return new DocumentLoadResult
            {
                DocumentState = nextState,
                Blocks = blocks,
                ViewMode = currentViewMode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Markdown parsing failed. Falling back to plaintext paragraph rendering.");
            return new DocumentLoadResult
            {
                DocumentState = nextState,
                Blocks = [new MarkdownBlock { Type = BlockType.Paragraph, Content = document.Content }],
                ViewMode = currentViewMode == EditorViewMode.RichTextEditor ? EditorViewMode.TextEditor : currentViewMode,
                InlineErrorMessage = "Markdown parsing failed. The document is shown in a safe fallback mode."
            };
        }
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
