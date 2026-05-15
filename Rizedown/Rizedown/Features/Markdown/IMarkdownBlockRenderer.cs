using Rizedown.Models;

namespace Rizedown.Features.Markdown;

public interface IMarkdownBlockRenderer
{
    bool CanRender(BlockType blockType);
    View Render(MarkdownBlock block, MarkdownRenderContext context);
}
