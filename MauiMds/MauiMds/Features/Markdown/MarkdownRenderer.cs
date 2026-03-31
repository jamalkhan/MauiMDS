using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class MarkdownRenderer
{
    private readonly IReadOnlyList<IMarkdownBlockRenderer> _renderers;

    public MarkdownRenderer(IEnumerable<IMarkdownBlockRenderer> renderers)
    {
        _renderers = renderers.ToList();
    }

    public IEnumerable<View> RenderBlocks(IEnumerable<MarkdownBlock> blocks, MarkdownRenderContext context)
    {
        foreach (var block in blocks)
        {
            var view = RenderBlock(block, context);
            if (view is not null)
            {
                yield return view;
            }
        }
    }

    public View? RenderBlock(MarkdownBlock block, MarkdownRenderContext context)
    {
        if (context.RenderMode == MarkdownRenderMode.Simplified)
        {
            return MarkdownViewFactory.CreateSimplifiedBlockView(block);
        }

        var renderer = _renderers.FirstOrDefault(candidate => candidate.CanRender(block.Type));
        return renderer?.Render(block, context);
    }
}
