using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class FootnoteBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Footnote;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var marker = MarkdownViewFactory.CreateRichTextLabel($"[{block.FootnoteId}]", 12, FontAttributes.Bold, new Thickness(0), context.InlineFormatter);
        var content = MarkdownViewFactory.CreateRichTextLabel(block.Content, 14, FontAttributes.None, new Thickness(0), context.InlineFormatter);

        layout.Children.Add(marker);
        layout.Children.Add(content);
        Grid.SetColumn(content, 1);
        return layout;
    }
}
