using MauiMds.Models;
using Microsoft.Maui.Controls.Shapes;

namespace MauiMds.Markdown;

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

        var grid = new Grid
        {
            ColumnSpacing = 0,
            RowSpacing = 0
        };

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var rowIndex = 0; rowIndex < block.TableRows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var headerCell = CreateTableCell(
                columnIndex < block.TableHeaders.Count ? block.TableHeaders[columnIndex] : string.Empty,
                columnIndex < block.TableAlignments.Count ? block.TableAlignments[columnIndex] : MarkdownAlignment.Left,
                true,
                columnIndex == columnCount - 1,
                block.TableRows.Count == 0,
                context);
            grid.Children.Add(headerCell);
            Grid.SetColumn(headerCell, columnIndex);
            Grid.SetRow(headerCell, 0);
        }

        for (var rowIndex = 0; rowIndex < block.TableRows.Count; rowIndex++)
        {
            var row = block.TableRows[rowIndex];
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var dataCell = CreateTableCell(
                    columnIndex < row.Count ? row[columnIndex] : string.Empty,
                    columnIndex < block.TableAlignments.Count ? block.TableAlignments[columnIndex] : MarkdownAlignment.Left,
                    false,
                    columnIndex == columnCount - 1,
                    rowIndex == block.TableRows.Count - 1,
                    context);
                grid.Children.Add(dataCell);
                Grid.SetColumn(dataCell, columnIndex);
                Grid.SetRow(dataCell, rowIndex + 1);
            }
        }

        var tableBorder = new Border
        {
            Padding = new Thickness(0),
            StrokeThickness = 1,
            Content = grid,
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

    private static Border CreateTableCell(string text, MarkdownAlignment alignment, bool isHeader, bool isLastColumn, bool isLastRow, MarkdownRenderContext context)
    {
        var label = MarkdownViewFactory.CreateRichTextLabel(text, isHeader ? 14 : 13, isHeader ? FontAttributes.Bold : FontAttributes.None, new Thickness(0), context.InlineFormatter);
        label.HorizontalTextAlignment = alignment switch
        {
            MarkdownAlignment.Center => TextAlignment.Center,
            MarkdownAlignment.Right => TextAlignment.End,
            _ => TextAlignment.Start
        };

        var border = new Border
        {
            Padding = new Thickness(12, 10),
            Content = label,
            StrokeShape = new Rectangle(),
            StrokeThickness = 0
        };

        border.SetAppThemeColor(VisualElement.BackgroundColorProperty,
            isHeader ? Color.FromArgb("#EDE4D4") : Color.FromArgb("#F8F3E8"),
            isHeader ? Color.FromArgb("#35363A") : Color.FromArgb("#2A2B2D"));

        border.Stroke = Brush.Transparent;

        if (!isLastColumn || !isLastRow)
        {
            border.StrokeThickness = 1;
            border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8CEBB"), Color.FromArgb("#4A4B50"));
        }

        return border;
    }
}
