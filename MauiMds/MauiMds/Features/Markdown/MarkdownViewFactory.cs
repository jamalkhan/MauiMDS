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
}
