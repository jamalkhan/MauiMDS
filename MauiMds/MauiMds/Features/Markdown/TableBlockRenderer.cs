using MauiMds.Models;
using Microsoft.Maui.Controls.Shapes;

namespace MauiMds.Features.Markdown;

public sealed class TableBlockRenderer : IMarkdownBlockRenderer
{
    public bool CanRender(BlockType blockType) => blockType == BlockType.Table;

    public View Render(MarkdownBlock block, MarkdownRenderContext context)
    {
        var columnCount = Math.Max(
            block.TableHeaders.Count,
            block.TableRows.Count == 0 ? 0 : block.TableRows.Max(row => row.Count));

        if (columnCount == 0)
        {
            return MarkdownViewFactory.CreateRichTextLabel(block.Content, 18, FontAttributes.None, new Thickness(0, 0, 0, 8), context.InlineFormatter);
        }

        var tableText = BuildTableText(block, columnCount);
        var label = new Label
        {
            Text = tableText,
            FontFamily = "Courier New",
            FontSize = 13,
            LineBreakMode = LineBreakMode.NoWrap,
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#1E1E1E"), Color.FromArgb("#F5F1E8"));

        var tableBorder = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            Content = label,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(14)
            }
        };
        tableBorder.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F8F3E8"), Color.FromArgb("#2A2B2D"));
        tableBorder.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8CEBB"), Color.FromArgb("#4A4B50"));

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 12),
            Content = tableBorder
        };
    }

    private static string BuildTableText(MarkdownBlock block, int columnCount)
    {
        var widths = new int[columnCount];

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            widths[columnIndex] = columnIndex < block.TableHeaders.Count ? block.TableHeaders[columnIndex].Length : 0;
        }

        foreach (var row in block.TableRows)
        {
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var cell = columnIndex < row.Count ? row[columnIndex] : string.Empty;
                widths[columnIndex] = Math.Max(widths[columnIndex], cell.Length);
            }
        }

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            widths[columnIndex] = Math.Max(widths[columnIndex], 3);
        }

        var lines = new List<string>
        {
            BuildTableLine(block.TableHeaders, widths, columnCount),
            BuildSeparatorLine(block.TableAlignments, widths, columnCount)
        };

        foreach (var row in block.TableRows)
        {
            lines.Add(BuildTableLine(row, widths, columnCount));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTableLine(IReadOnlyList<string> cells, IReadOnlyList<int> widths, int columnCount)
    {
        var renderedCells = new string[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var value = columnIndex < cells.Count ? cells[columnIndex] : string.Empty;
            if (value.Length > widths[columnIndex])
            {
                value = value[..Math.Max(0, widths[columnIndex] - 1)] + "…";
            }

            renderedCells[columnIndex] = value.PadRight(widths[columnIndex]);
        }

        return $"| {string.Join(" | ", renderedCells)} |";
    }

    private static string BuildSeparatorLine(IReadOnlyList<MarkdownAlignment> alignments, IReadOnlyList<int> widths, int columnCount)
    {
        var segments = new string[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var width = widths[columnIndex];
            var alignment = columnIndex < alignments.Count ? alignments[columnIndex] : MarkdownAlignment.Left;
            segments[columnIndex] = alignment switch
            {
                MarkdownAlignment.Center => ":" + new string('-', Math.Max(1, width - 2)) + ":",
                MarkdownAlignment.Right => new string('-', Math.Max(1, width - 1)) + ":",
                _ => ":" + new string('-', Math.Max(1, width - 1))
            };
        }

        return $"| {string.Join(" | ", segments)} |";
    }
}
