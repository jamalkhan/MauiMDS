using Microsoft.Maui.Controls.Shapes;

namespace MauiMds.Features.Markdown;

internal static class MarkdownViewFactory
{
    public static Label CreateBaseLabel()
    {
        var label = new Label
        {
            TextColor = Colors.Black,
            Margin = new Thickness(0, 0, 0, 8),
            LineBreakMode = LineBreakMode.WordWrap
        };

        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#161616"), Color.FromArgb("#F3EDE2"));
        return label;
    }

    public static Label CreateRichTextLabel(string content, double fontSize, FontAttributes fontAttributes, Thickness margin, MarkdownInlineFormatter inlineFormatter)
    {
        var label = CreateBaseLabel();
        label.FontSize = fontSize;
        label.FontAttributes = fontAttributes;
        label.Margin = margin;

        if (inlineFormatter.RequiresFormattedText(content))
        {
            label.FormattedText = inlineFormatter.BuildFormattedText(content, fontSize);
        }
        else
        {
            label.Text = content;
        }

        return label;
    }

    public static Border CreateThemedBorder(View content, Thickness padding, Thickness margin, float cornerRadius = 16, bool stroked = true)
    {
        var border = new Border
        {
            Content = content,
            Padding = padding,
            Margin = margin,
            StrokeThickness = stroked ? 1 : 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(cornerRadius)
            }
        };

        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F8F3E8"), Color.FromArgb("#2A2B2D"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8CEBB"), Color.FromArgb("#4A4B50"));
        return border;
    }

    public static View CreateSimplifiedBlockView(MauiMds.Models.MarkdownBlock block)
    {
        return block.Type switch
        {
            MauiMds.Models.BlockType.Header => CreateSimpleLabel(block.Content, GetHeaderFontSize(block.HeaderLevel), FontAttributes.Bold, new Thickness(0, block.HeaderLevel == 1 ? 4 : 16, 0, 8), monospace: false),
            MauiMds.Models.BlockType.BulletListItem => CreateSimpleLabel($"{new string(' ', block.ListLevel * 2)}• {block.Content}", 17, FontAttributes.None, new Thickness(0, 0, 0, 4), monospace: false),
            MauiMds.Models.BlockType.OrderedListItem => CreateSimpleLabel($"{new string(' ', block.ListLevel * 2)}{block.OrderedNumber}. {block.Content}", 17, FontAttributes.None, new Thickness(0, 0, 0, 4), monospace: false),
            MauiMds.Models.BlockType.TaskListItem => CreateSimpleLabel($"{new string(' ', block.ListLevel * 2)}[{(block.IsChecked ? "x" : " ")}] {block.Content}", 17, FontAttributes.None, new Thickness(0, 0, 0, 6), monospace: false),
            MauiMds.Models.BlockType.BlockQuote => CreateSimpleQuoteView(block),
            MauiMds.Models.BlockType.CodeBlock => CreateSimpleMonospaceBlock(string.IsNullOrWhiteSpace(block.CodeLanguage) ? block.Content : $"{block.CodeLanguage}{Environment.NewLine}{block.Content}"),
            MauiMds.Models.BlockType.Table => CreateSimpleMonospaceBlock(block.Content),
            MauiMds.Models.BlockType.Image => CreateSimpleImagePlaceholder(block),
            MauiMds.Models.BlockType.HorizontalRule => CreateSimpleRule(),
            MauiMds.Models.BlockType.Footnote => CreateSimpleLabel($"[{block.FootnoteId}] {block.Content}", 14, FontAttributes.None, new Thickness(0, 4, 0, 8), monospace: false),
            MauiMds.Models.BlockType.Admonition => CreateSimpleAdmonitionView(block),
            MauiMds.Models.BlockType.DefinitionTerm => CreateSimpleLabel(block.Content, 17, FontAttributes.Bold, new Thickness(0, 8, 0, 2), monospace: false),
            MauiMds.Models.BlockType.DefinitionDetail => CreateSimpleLabel($"  : {block.Content}", 17, FontAttributes.None, new Thickness(0, 0, 0, 4), monospace: false),
            _ => CreateSimpleLabel(block.Content, 18, FontAttributes.None, new Thickness(0, 0, 0, 8), monospace: false)
        };
    }

    private static View CreateSimpleAdmonitionView(MauiMds.Models.MarkdownBlock block)
    {
        var label = CreateSimpleLabel($"{block.AdmonitionType}: {block.Content}", 17, FontAttributes.None, new Thickness(0), monospace: false);
        var border = CreateThemedBorder(label, new Thickness(14, 10), new Thickness(0, 4, 0, 10));
        return border;
    }

    private static View CreateSimpleQuoteView(MauiMds.Models.MarkdownBlock block)
    {
        var label = CreateSimpleLabel(block.Content, 17, FontAttributes.None, new Thickness(0), monospace: false);
        var border = CreateThemedBorder(label, new Thickness(18, 14, 14, 14), new Thickness(0, 4, 0, 10), stroked: false);
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EFE7D8"), Color.FromArgb("#343432"));
        border.StrokeThickness = Math.Max(3, block.QuoteLevel * 2);
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#A08E71"), Color.FromArgb("#C8B79D"));
        return border;
    }

    private static View CreateSimpleMonospaceBlock(string content)
    {
        var label = CreateSimpleLabel(content, 13, FontAttributes.None, new Thickness(0), monospace: true);
        label.LineBreakMode = LineBreakMode.NoWrap;
        var border = CreateThemedBorder(label, new Thickness(14, 12), new Thickness(0, 4, 0, 12));
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EAE3D6"), Color.FromArgb("#1E1F21"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#CFC3AE"), Color.FromArgb("#4A4C52"));
        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = border
        };
    }

    private static View CreateSimpleImagePlaceholder(MauiMds.Models.MarkdownBlock block)
    {
        var text = !string.IsNullOrWhiteSpace(block.ImageAltText)
            ? $"[Image] {block.ImageAltText}"
            : $"[Image] {block.ImageSource}";

        var label = CreateSimpleLabel(text, 13, FontAttributes.Italic, new Thickness(0), monospace: false);
        return CreateThemedBorder(label, new Thickness(14, 12), new Thickness(0, 6, 0, 12));
    }

    private static View CreateSimpleRule()
    {
        var rule = new BoxView
        {
            HeightRequest = 1,
            Margin = new Thickness(0, 8, 0, 12)
        };
        rule.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#CFC3AE"), Color.FromArgb("#4A4C52"));
        return rule;
    }

    private static Label CreateSimpleLabel(string content, double fontSize, FontAttributes attributes, Thickness margin, bool monospace)
    {
        var label = CreateBaseLabel();
        label.Text = content;
        label.FontSize = fontSize;
        label.FontAttributes = attributes;
        label.Margin = margin;
        if (monospace)
        {
            label.FontFamily = "Courier New";
        }

        return label;
    }

    private static double GetHeaderFontSize(int headerLevel)
    {
        return headerLevel switch
        {
            1 => 32,
            2 => 26,
            3 => 22,
            4 => 20,
            5 => 18,
            _ => 16
        };
    }
}
