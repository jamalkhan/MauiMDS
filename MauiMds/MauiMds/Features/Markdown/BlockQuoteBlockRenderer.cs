using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class BlockQuoteBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.BlockQuote;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var quoteLabel = MarkdownViewFactory.CreateRichTextLabel(block.Content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter);

        var accent = new BoxView
        {
            WidthRequest = Math.Max(4, block.QuoteLevel * 3),
            CornerRadius = 2,
            MinimumHeightRequest = 24,
            VerticalOptions = LayoutOptions.Start
        };
        accent.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#A08E71"), Color.FromArgb("#C8B79D"));

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 14
        };
        layout.Children.Add(accent);
        layout.Children.Add(quoteLabel);
        Grid.SetColumn(quoteLabel, 1);

        var border = MarkdownViewFactory.CreateThemedBorder(layout, new Thickness(16, 14), new Thickness(0, 4, 0, 10), stroked: false);
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EFE7D8"), Color.FromArgb("#343432"));
        return border;
    }
}
