using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public interface IMarkdownBlockRenderer
{
    bool CanRender(BlockType blockType);
    View Render(MarkdownBlock block, MarkdownRenderContext context);
}
