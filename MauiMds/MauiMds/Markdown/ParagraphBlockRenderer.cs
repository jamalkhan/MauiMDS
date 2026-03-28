using MauiMds.Models;

namespace MauiMds.Markdown;

public sealed class ParagraphBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Paragraph;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        return MarkdownViewFactory.CreateRichTextLabel(block.Content, 18, FontAttributes.None, new Thickness(0, 0, 0, 8), context.InlineFormatter);
    }
}
