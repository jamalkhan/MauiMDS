using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class BlockQuoteBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.BlockQuote;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        View innerContent;

        if (block.Children.Count > 0)
        {
            var childRenderer = new MarkdownRenderer(
            [
                new HeaderBlockRenderer(),
                new ParagraphBlockRenderer(),
                new ListBlockRenderer(),
                new CodeBlockRenderer(),
                new TableBlockRenderer(),
                new HorizontalRuleBlockRenderer(),
                new ImageBlockRenderer()
            ]);

            var childStack = new VerticalStackLayout { Spacing = 8 };
            foreach (var child in block.Children)
            {
                var childView = childRenderer.RenderBlock(child, context);
                if (childView is not null)
                {
                    childStack.Children.Add(childView);
                }
            }
            innerContent = childStack;
        }
        else
        {
            innerContent = MarkdownViewFactory.CreateRichTextLabel(block.Content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter);
        }

        var border = MarkdownViewFactory.CreateThemedBorder(innerContent, new Thickness(18, 14, 14, 14), new Thickness(0, 4, 0, 10), stroked: false);
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EFE7D8"), Color.FromArgb("#343432"));
        border.StrokeThickness = Math.Max(3, block.QuoteLevel * 2);
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#A08E71"), Color.FromArgb("#C8B79D"));
        return border;
    }
}
