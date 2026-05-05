using MauiMds;
using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class FrontMatterBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.FrontMatter;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var title = MarkdownViewFactory.CreateBaseLabel();
        title.Text = "Frontmatter";
        title.FontSize = 12;
        title.FontAttributes = FontAttributes.Bold;
        title.Margin = new Thickness(0, 0, 0, 8);
        title.SetAppThemeColor(Label.TextColorProperty, AppColors.FrontMatterTitleLight, AppColors.FrontMatterTitleDark);

        var content = new Label
        {
            FontFamily = "Courier New",
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0)
        };
        content.SetAppThemeColor(Label.TextColorProperty, AppColors.FrontMatterContentLight, AppColors.FrontMatterContentDark);
        content.Text = block.Content;

        var stack = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                title,
                content
            }
        };

        var border = MarkdownViewFactory.CreateThemedBorder(stack, new Thickness(14, 12), new Thickness(0, 0, 0, 12));
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, AppColors.FrontMatterBgLight, AppColors.FrontMatterBgDark);
        border.SetAppThemeColor(Border.StrokeProperty, AppColors.FrontMatterBorderLight, AppColors.FrontMatterBorderDark);
        return border;
    }
}
