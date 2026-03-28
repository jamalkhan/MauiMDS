using MauiMds.Models;

namespace MauiMds.Markdown;

public sealed class MarkdownRenderContext
{
    public required string SourceFilePath { get; init; }
    public required MarkdownInlineFormatter InlineFormatter { get; init; }
}
