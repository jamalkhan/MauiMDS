using MauiMds.Models;

namespace MauiMds.Features.Markdown;

public sealed class CodeBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.CodeBlock;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var codeLabel = new Label
        {
            FontFamily = "Courier New",
            FontSize = 15,
            LineBreakMode = LineBreakMode.NoWrap,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            FormattedText = context.InlineFormatter.BuildCodeFormattedText(block.Content, block.CodeLanguage)
        };
        codeLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#1E1E1E"), Color.FromArgb("#F5F1E8"));

        var stack = new VerticalStackLayout
        {
            Spacing = 8
        };

        if (!string.IsNullOrWhiteSpace(block.CodeLanguage))
        {
            var languageLabel = new Label
            {
                Text = block.CodeLanguage,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                Margin = new Thickness(0)
            };
            languageLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#7B735F"), Color.FromArgb("#CDBEA3"));
            stack.Children.Add(languageLabel);
        }

        stack.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            Content = codeLabel
        });

        var border = MarkdownViewFactory.CreateThemedBorder(stack, new Thickness(16, 14), new Thickness(0, 4, 0, 12));
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EAE3D6"), Color.FromArgb("#1E1F21"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#CFC3AE"), Color.FromArgb("#4A4C52"));
        return border;
    }
}
