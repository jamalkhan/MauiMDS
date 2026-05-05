using MauiMds;
using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class DefinitionListRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType)
    {
        return blockType is BlockType.DefinitionTerm or BlockType.DefinitionDetail;
    }

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        return block.Type == BlockType.DefinitionTerm
            ? RenderTerm(block, context)
            : RenderDetail(block, context);
    }

    private static View RenderTerm(MarkdownBlock block, MarkdownRenderContext context)
    {
        return MarkdownViewFactory.CreateRichTextLabel(
            block.Content,
            17,
            FontAttributes.Bold,
            new Thickness(0, 8, 0, 2),
            context.InlineFormatter);
    }

    private static View RenderDetail(MarkdownBlock block, MarkdownRenderContext context)
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(16, GridUnitType.Absolute)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 4,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var marker = MarkdownViewFactory.CreateBaseLabel();
        marker.FontSize = 17;
        marker.Text = ":";
        marker.SetAppThemeColor(Label.TextColorProperty, AppColors.CodeLangLight, AppColors.CodeLangDark);
        marker.Margin = new Thickness(4, 0, 0, 0);

        var content = MarkdownViewFactory.CreateRichTextLabel(
            block.Content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter);

        layout.Add(marker);
        layout.Add(content);
        Grid.SetColumn(content, 1);

        return layout;
    }
}
