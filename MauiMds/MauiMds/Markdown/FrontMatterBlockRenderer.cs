using MauiMds.Models;

namespace MauiMds.Markdown;

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
        title.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#7B5A2A"), Color.FromArgb("#E6C88A"));

        var content = new Label
        {
            FontFamily = "Courier New",
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0)
        };
        content.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#2C261E"), Color.FromArgb("#F0E7D9"));
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
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F0E5D2"), Color.FromArgb("#352F29"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#CCB28A"), Color.FromArgb("#675843"));
        return border;
    }
}
