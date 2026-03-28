using MauiMds.Models;

namespace MauiMds.Markdown;

public sealed class HorizontalRuleBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.HorizontalRule;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var rule = new BoxView
        {
            HeightRequest = 1,
            Margin = new Thickness(0, 10, 0, 14)
        };
        rule.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#CDBFA7"), Color.FromArgb("#4A4B50"));
        return rule;
    }
}
