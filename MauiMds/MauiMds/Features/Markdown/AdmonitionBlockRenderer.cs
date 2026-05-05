using MauiMds.Models;
using Microsoft.Maui.Controls.Shapes;

namespace MauiMds.Features.Markdown;

public sealed class AdmonitionBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Admonition;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var (bgLight, bgDark, borderLight, borderDark, headerLight, headerDark) = GetThemeColors(block.AdmonitionType);

        var headerText = string.IsNullOrEmpty(block.AdmonitionTitle)
            ? FormatTypeLabel(block.AdmonitionType)
            : block.AdmonitionTitle;
        var typeLabel = new Label
        {
            Text = headerText,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        typeLabel.SetAppThemeColor(Label.TextColorProperty, headerLight, headerDark);

        var contentLabel = MarkdownViewFactory.CreateRichTextLabel(
            block.Content, 17, FontAttributes.None, new Thickness(0), context.InlineFormatter);

        var stack = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { typeLabel, contentLabel }
        };

        var border = new Border
        {
            Content = stack,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 4, 0, 10),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) }
        };

        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, bgLight, bgDark);
        border.SetAppThemeColor(Border.StrokeProperty, borderLight, borderDark);

        // Left accent bar
        var accent = new BoxView
        {
            WidthRequest = 4,
            CornerRadius = 2,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0)
        };
        accent.SetAppThemeColor(BoxView.ColorProperty, borderLight, borderDark);

        var outerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(4)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 0,
            Margin = new Thickness(0, 4, 0, 10)
        };

        outerGrid.SetAppThemeColor(VisualElement.BackgroundColorProperty, bgLight, bgDark);

        // Use a simpler single-border approach with left emphasis via padding offset
        var innerBorder = new Border
        {
            Content = stack,
            Padding = new Thickness(14, 10, 14, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 10, 10, 0) }
        };
        innerBorder.SetAppThemeColor(VisualElement.BackgroundColorProperty, bgLight, bgDark);

        outerGrid.Add(accent);
        outerGrid.Add(innerBorder);
        Grid.SetColumn(innerBorder, 1);

        return outerGrid;
    }

    private static string FormatTypeLabel(string admonitionType)
    {
        return admonitionType.ToUpperInvariant() switch
        {
            "NOTE" => "NOTE",
            "TIP" => "TIP",
            "WARNING" => "WARNING",
            "IMPORTANT" => "IMPORTANT",
            "CAUTION" => "CAUTION",
            "INFO" => "INFO",
            "DANGER" => "DANGER",
            "SUCCESS" => "SUCCESS",
            "BUG" => "BUG",
            "EXAMPLE" => "EXAMPLE",
            "QUESTION" => "QUESTION",
            "ABSTRACT" => "ABSTRACT",
            "TLDR" => "TL;DR",
            _ => admonitionType.ToUpperInvariant()
        };
    }

    private static (Color BgLight, Color BgDark, Color BorderLight, Color BorderDark, Color HeaderLight, Color HeaderDark)
        GetThemeColors(string admonitionType)
    {
        return admonitionType.ToUpperInvariant() switch
        {
            "NOTE" or "INFO"             => AppColors.AdmonitionNote,
            "TIP" or "SUCCESS"           => AppColors.AdmonitionTip,
            "WARNING"                    => AppColors.AdmonitionWarning,
            "IMPORTANT"                  => AppColors.AdmonitionImportant,
            "CAUTION" or "DANGER"        => AppColors.AdmonitionCaution,
            "QUESTION"                   => AppColors.AdmonitionQuestion,
            "BUG"                        => AppColors.AdmonitionBug,
            _                            => AppColors.AdmonitionDefault
        };
    }
}
