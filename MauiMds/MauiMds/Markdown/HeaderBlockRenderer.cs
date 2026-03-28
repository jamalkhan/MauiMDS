using MauiMds.Models;

namespace MauiMds.Markdown;

public sealed class HeaderBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Header;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var fontSize = block.HeaderLevel switch
        {
            1 => 32,
            2 => 26,
            3 => 22,
            4 => 20,
            5 => 18,
            _ => 16
        };

        return MarkdownViewFactory.CreateRichTextLabel(
            block.Content,
            fontSize,
            FontAttributes.Bold,
            new Thickness(0, block.HeaderLevel == 1 ? 4 : 16, 0, 8),
            context.InlineFormatter);
    }
}
