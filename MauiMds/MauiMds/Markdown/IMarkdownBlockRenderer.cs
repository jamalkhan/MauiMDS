using MauiMds.Models;

namespace MauiMds.Markdown;

public interface IMarkdownBlockRenderer
{
    bool CanRender(BlockType blockType);
    View Render(MarkdownBlock block, MarkdownRenderContext context);
}
