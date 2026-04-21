using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public enum MarkdownRenderMode
{
    Full,
    Simplified
}

public sealed class MarkdownRenderContext
{
    public required string SourceFilePath { get; init; }
    public required MarkdownInlineFormatter InlineFormatter { get; init; }
    public MarkdownRenderMode RenderMode { get; init; } = MarkdownRenderMode.Full;
    public Action<string>? NavigateToAnchor { get; init; }
}
