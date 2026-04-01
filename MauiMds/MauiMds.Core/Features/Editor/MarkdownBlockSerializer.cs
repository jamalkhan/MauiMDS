using System.Text;
using MauiMds.Models;

namespace MauiMds.Features.Editor;

public sealed class MarkdownBlockSerializer
{
    public string Serialize(IReadOnlyList<MarkdownBlock> blocks, string newLine = "\n")
    {
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < blocks.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(newLine);
                builder.Append(newLine);
            }

            builder.Append(SerializeBlock(blocks[index], newLine));
        }

        return builder.ToString();
    }

    public string SerializeBlock(MarkdownBlock block, string newLine = "\n")
    {
        return block.Type switch
        {
            BlockType.FrontMatter => $"---{newLine}{block.Content}{newLine}---",
            BlockType.Header => $"{new string('#', Math.Clamp(block.HeaderLevel, 1, 6))} {block.Content}".TrimEnd(),
            BlockType.Paragraph => block.Content,
            BlockType.BulletListItem => $"{Indent(block.ListLevel)}- {block.Content}".TrimEnd(),
            BlockType.OrderedListItem => $"{Indent(block.ListLevel)}{Math.Max(1, block.OrderedNumber)}. {block.Content}".TrimEnd(),
            BlockType.TaskListItem => $"{Indent(block.ListLevel)}- [{(block.IsChecked ? "x" : " ")}] {block.Content}".TrimEnd(),
            BlockType.BlockQuote => SerializeBlockQuote(block, newLine),
            BlockType.CodeBlock => SerializeCodeBlock(block, newLine),
            BlockType.Table => SerializeTable(block, newLine),
            BlockType.HorizontalRule => "---",
            BlockType.Image => $"![{block.ImageAltText}]({block.ImageSource})",
            BlockType.Footnote => $"[^{block.FootnoteId}]: {block.Content}".TrimEnd(),
            _ => block.Content
        };
    }

    private static string SerializeBlockQuote(MarkdownBlock block, string newLine)
    {
        var prefix = string.Concat(Enumerable.Repeat("> ", Math.Max(1, block.QuoteLevel)));
        var lines = Normalize(block.Content).Split('\n');
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(newLine);
            }

            builder.Append(prefix);
            builder.Append(lines[index]);
        }

        return builder.ToString();
    }

    private static string SerializeCodeBlock(MarkdownBlock block, string newLine)
    {
        var builder = new StringBuilder();
        builder.Append("```");
        builder.Append(block.CodeLanguage);
        builder.Append(newLine);
        builder.Append(block.Content);
        builder.Append(newLine);
        builder.Append("```");
        return builder.ToString();
    }

    private static string SerializeTable(MarkdownBlock block, string newLine)
    {
        var builder = new StringBuilder();
        builder.Append("| ");
        builder.Append(string.Join(" | ", block.TableHeaders));
        builder.Append(" |");
        builder.Append(newLine);
        builder.Append("| ");
        builder.Append(string.Join(" | ", BuildDividerCells(block)));
        builder.Append(" |");

        foreach (var row in block.TableRows)
        {
            builder.Append(newLine);
            builder.Append("| ");
            builder.Append(string.Join(" | ", row));
            builder.Append(" |");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> BuildDividerCells(MarkdownBlock block)
    {
        if (block.TableHeaders.Count == 0)
        {
            return ["---"];
        }

        return Enumerable.Range(0, block.TableHeaders.Count).Select(index =>
        {
            var alignment = index < block.TableAlignments.Count ? block.TableAlignments[index] : MarkdownAlignment.Left;
            return alignment switch
            {
                MarkdownAlignment.Center => ":---:",
                MarkdownAlignment.Right => "---:",
                _ => ":---"
            };
        });
    }

    private static string Normalize(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string Indent(int listLevel)
    {
        return new string(' ', Math.Max(0, listLevel) * 2);
    }
}
