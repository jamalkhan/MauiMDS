using MauiMds;
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
            Padding = new Thickness(0)
        };

        if (!string.IsNullOrWhiteSpace(block.CodeLanguage))
        {
            codeLabel.FormattedText = context.InlineFormatter.BuildCodeFormattedText(block.Content, block.CodeLanguage);
        }
        else
        {
            codeLabel.SetAppThemeColor(Label.TextColorProperty, AppColors.MonoTextLight, AppColors.MonoTextDark);
            codeLabel.Text = block.Content;
        }

        var stack = new VerticalStackLayout
        {
            Spacing = 6
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
            languageLabel.SetAppThemeColor(Label.TextColorProperty, AppColors.CodeLangLight, AppColors.CodeLangDark);
            stack.Children.Add(languageLabel);
        }

        stack.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            Content = codeLabel
        });

        var border = MarkdownViewFactory.CreateThemedBorder(stack, new Thickness(16, 14), new Thickness(0, 4, 0, 12));
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, AppColors.CodeBgLight, AppColors.CodeBgDark);
        border.SetAppThemeColor(Border.StrokeProperty, AppColors.CodeBorderLight, AppColors.CodeBorderDark);
        return border;
    }
}
