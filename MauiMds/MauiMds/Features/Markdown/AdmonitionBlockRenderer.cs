using MauiMds.Models;
using Microsoft.Maui.Controls.Shapes;

namespace MauiMds.Features.Markdown;

public sealed class AdmonitionBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Admonition;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var (lightBg, darkBg, lightBorder, darkBorder, lightHeader, darkHeader) = GetThemeColors(block.AdmonitionType);

        var typeLabel = new Label
        {
            Text = FormatTypeLabel(block.AdmonitionType),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        typeLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb(lightHeader), Color.FromArgb(darkHeader));

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

        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb(lightBg), Color.FromArgb(darkBg));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb(lightBorder), Color.FromArgb(darkBorder));

        // Left accent bar
        var accent = new BoxView
        {
            WidthRequest = 4,
            CornerRadius = 2,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0)
        };
        accent.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb(lightBorder), Color.FromArgb(darkBorder));

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

        outerGrid.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb(lightBg), Color.FromArgb(darkBg));

        // Use a simpler single-border approach with left emphasis via padding offset
        var innerBorder = new Border
        {
            Content = stack,
            Padding = new Thickness(14, 10, 14, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 10, 10, 0) }
        };
        innerBorder.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb(lightBg), Color.FromArgb(darkBg));

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

    private static (string lightBg, string darkBg, string lightBorder, string darkBorder, string lightHeader, string darkHeader) GetThemeColors(string admonitionType)
    {
        return admonitionType.ToUpperInvariant() switch
        {
            "NOTE" or "INFO" =>
                ("#EFF6FF", "#1A2A3F", "#3B82F6", "#60A5FA", "#1D4ED8", "#93C5FD"),
            "TIP" or "SUCCESS" =>
                ("#F0FDF4", "#1A2E22", "#22C55E", "#4ADE80", "#15803D", "#86EFAC"),
            "WARNING" =>
                ("#FFFBEB", "#2E2410", "#F59E0B", "#FCD34D", "#B45309", "#FDE68A"),
            "IMPORTANT" =>
                ("#F5F3FF", "#1E1533", "#8B5CF6", "#A78BFA", "#6D28D9", "#C4B5FD"),
            "CAUTION" or "DANGER" =>
                ("#FEF2F2", "#2A1010", "#EF4444", "#F87171", "#B91C1C", "#FCA5A5"),
            "QUESTION" =>
                ("#ECFDF5", "#102A22", "#10B981", "#34D399", "#047857", "#6EE7B7"),
            "BUG" =>
                ("#FFF7ED", "#2A1800", "#F97316", "#FB923C", "#C2410C", "#FED7AA"),
            "EXAMPLE" or "ABSTRACT" or "TLDR" =>
                ("#F8FAFC", "#1C1C24", "#64748B", "#94A3B8", "#334155", "#CBD5E1"),
            _ =>
                ("#F8FAFC", "#1C1C24", "#64748B", "#94A3B8", "#334155", "#CBD5E1")
        };
    }
}
