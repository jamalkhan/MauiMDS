using System.Text;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Processors;

public class MdsParser
{
    private readonly ILogger<MdsParser> _logger;

    public MdsParser(ILogger<MdsParser> logger)
    {
        _logger = logger;
    }

    public List<MarkdownBlock> Parse(string content)
    {
        _logger.LogInformation("Starting markdown parse. CharacterCount: {CharacterCount}", content.Length);

        var normalizedContent = NormalizeNewLines(content);
        var lines = normalizedContent.Split('\n');
        var blocks = new List<MarkdownBlock>(Math.Max(8, lines.Length / 2));
        var footnotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentParagraph = new StringBuilder();

        var index = 0;
        if (TryExtractFrontMatter(lines, ref index, out var frontMatter))
        {
            blocks.Add(new MarkdownBlock
            {
                Type = BlockType.FrontMatter,
                Content = frontMatter
            });
        }

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmedLine = line.Trim();

            if (trimmedLine.Length == 0)
            {
                FlushParagraph(blocks, currentParagraph);
                index++;
                continue;
            }

            if (TryExtractFootnote(lines, ref index, trimmedLine, footnotes))
            {
                FlushParagraph(blocks, currentParagraph);
                continue;
            }

            if (IsCodeFence(trimmedLine))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(ParseCodeBlock(lines, ref index, trimmedLine));
                continue;
            }

            if (IsTableHeader(lines, index))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(ParseTable(lines, ref index));
                continue;
            }

            if (trimmedLine[0] == '>')
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(ParseBlockQuote(lines, ref index));
                continue;
            }

            if (IsHorizontalRule(trimmedLine))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(new MarkdownBlock { Type = BlockType.HorizontalRule });
                index++;
                continue;
            }

            if (TryParseHeader(trimmedLine, out var headerBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(headerBlock);
                index++;
                continue;
            }

            if (TryParseImage(trimmedLine, out var imageBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(imageBlock);
                index++;
                continue;
            }

            var listLevel = GetListLevel(line);

            if (TryParseTaskList(trimmedLine, listLevel, out var taskBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(taskBlock);
                index++;
                continue;
            }

            if (TryParseOrderedList(trimmedLine, listLevel, out var orderedBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(orderedBlock);
                index++;
                continue;
            }

            if (TryParseBulletList(trimmedLine, listLevel, out var bulletBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                blocks.Add(bulletBlock);
                index++;
                continue;
            }

            AppendParagraphLine(currentParagraph, trimmedLine);
            index++;
        }

        FlushParagraph(blocks, currentParagraph);
        AppendFootnotes(blocks, footnotes);

        _logger.LogInformation("Completed markdown parse. LineCount: {LineCount}, BlockCount: {BlockCount}", lines.Length, blocks.Count);
        return blocks;
    }

    private static string NormalizeNewLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static bool TryExtractFrontMatter(string[] lines, ref int index, out string frontMatter)
    {
        frontMatter = string.Empty;
        if (lines.Length < 3)
        {
            return false;
        }

        var delimiter = lines[0].Trim();
        if (!string.Equals(delimiter, "---", StringComparison.Ordinal) &&
            !string.Equals(delimiter, "+++", StringComparison.Ordinal))
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var scanIndex = 1; scanIndex < lines.Length; scanIndex++)
        {
            if (string.Equals(lines[scanIndex].Trim(), delimiter, StringComparison.Ordinal))
            {
                frontMatter = builder.ToString().Trim();
                index = scanIndex + 1;
                return true;
            }

            if (builder.Length > 0)
            {
                builder.Append(Environment.NewLine);
            }

            builder.Append(lines[scanIndex]);
        }

        return false;
    }

    private static void FlushParagraph(List<MarkdownBlock> blocks, StringBuilder paragraphBuilder)
    {
        if (paragraphBuilder.Length == 0)
        {
            return;
        }

        blocks.Add(new MarkdownBlock
        {
            Type = BlockType.Paragraph,
            Content = paragraphBuilder.ToString()
        });

        paragraphBuilder.Clear();
    }

    private static void AppendParagraphLine(StringBuilder paragraphBuilder, string line)
    {
        if (paragraphBuilder.Length > 0)
        {
            paragraphBuilder.Append(' ');
        }

        paragraphBuilder.Append(line);
    }

    private static bool TryExtractFootnote(string[] lines, ref int index, string trimmedLine, Dictionary<string, string> footnotes)
    {
        if (!trimmedLine.StartsWith("[^", StringComparison.Ordinal))
        {
            return false;
        }

        var closingBracket = trimmedLine.IndexOf("]:", StringComparison.Ordinal);
        if (closingBracket <= 2)
        {
            return false;
        }

        var footnoteId = trimmedLine[2..closingBracket];
        var noteBuilder = new StringBuilder();
        var initialContent = trimmedLine[(closingBracket + 2)..].Trim();
        noteBuilder.Append(initialContent);

        index++;
        while (index < lines.Length)
        {
            var continuationLine = lines[index];

            if (string.IsNullOrWhiteSpace(continuationLine))
            {
                noteBuilder.Append(Environment.NewLine);
                index++;
                continue;
            }

            if (!continuationLine.StartsWith("    ", StringComparison.Ordinal) &&
                !continuationLine.StartsWith('\t'))
            {
                break;
            }

            if (noteBuilder.Length > 0)
            {
                noteBuilder.Append(Environment.NewLine);
            }

            noteBuilder.Append(continuationLine.Trim());
            index++;
        }

        footnotes[footnoteId] = noteBuilder.ToString().Trim();
        return true;
    }

    private static bool IsCodeFence(string trimmedLine)
    {
        return trimmedLine.StartsWith("```", StringComparison.Ordinal);
    }

    private static MarkdownBlock ParseCodeBlock(string[] lines, ref int index, string openingFenceLine)
    {
        var language = openingFenceLine.Length > 3 ? openingFenceLine[3..].Trim() : string.Empty;
        var builder = new StringBuilder();
        index++;

        while (index < lines.Length)
        {
            var currentLine = lines[index];
            if (currentLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                index++;
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append(Environment.NewLine);
            }

            builder.Append(currentLine);
            index++;
        }

        return new MarkdownBlock
        {
            Type = BlockType.CodeBlock,
            CodeLanguage = language,
            Content = builder.ToString()
        };
    }

    private static bool IsTableHeader(string[] lines, int index)
    {
        return index + 1 < lines.Length &&
               LooksLikeTableRow(lines[index]) &&
               IsTableDividerRow(lines[index + 1]);
    }

    private static MarkdownBlock ParseTable(string[] lines, ref int index)
    {
        var headers = ParseTableCells(lines[index]);
        var alignments = ParseTableAlignments(lines[index + 1]);
        var rows = new List<List<string>>();

        index += 2;
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || !LooksLikeTableRow(line))
            {
                break;
            }

            rows.Add(ParseTableCells(line));
            index++;
        }

        return new MarkdownBlock
        {
            Type = BlockType.Table,
            TableHeaders = headers,
            TableRows = rows,
            TableAlignments = alignments
        };
    }

    private static MarkdownBlock ParseBlockQuote(string[] lines, ref int index)
    {
        var quoteBuilder = new StringBuilder();
        var maxQuoteLevel = 1;

        while (index < lines.Length)
        {
            var sourceLine = lines[index];
            var trimmedLine = sourceLine.Trim();

            if (trimmedLine.Length == 0)
            {
                if (quoteBuilder.Length > 0)
                {
                    quoteBuilder.Append(Environment.NewLine);
                }

                index++;
                continue;
            }

            if (trimmedLine[0] != '>')
            {
                break;
            }

            var quoteLevel = 0;
            var contentStart = 0;
            while (contentStart < trimmedLine.Length && trimmedLine[contentStart] == '>')
            {
                quoteLevel++;
                contentStart++;
            }

            while (contentStart < trimmedLine.Length && trimmedLine[contentStart] == ' ')
            {
                contentStart++;
            }

            maxQuoteLevel = Math.Max(maxQuoteLevel, quoteLevel);
            if (quoteBuilder.Length > 0)
            {
                quoteBuilder.Append(Environment.NewLine);
            }

            quoteBuilder.Append(trimmedLine[contentStart..]);
            index++;
        }

        return new MarkdownBlock
        {
            Type = BlockType.BlockQuote,
            Content = quoteBuilder.ToString(),
            QuoteLevel = maxQuoteLevel
        };
    }

    private static bool TryParseHeader(string trimmedLine, out MarkdownBlock block)
    {
        block = null!;
        if (trimmedLine[0] != '#')
        {
            return false;
        }

        var level = 0;
        while (level < trimmedLine.Length && trimmedLine[level] == '#')
        {
            level++;
        }

        if (level >= trimmedLine.Length || trimmedLine[level] != ' ')
        {
            return false;
        }

        block = new MarkdownBlock
        {
            Type = BlockType.Header,
            HeaderLevel = Math.Min(level, 6),
            Content = trimmedLine[(level + 1)..].Trim()
        };

        return true;
    }

    private static bool TryParseImage(string trimmedLine, out MarkdownBlock block)
    {
        block = null!;
        if (!trimmedLine.StartsWith("![", StringComparison.Ordinal))
        {
            return false;
        }

        var altEnd = trimmedLine.IndexOf("](", StringComparison.Ordinal);
        if (altEnd < 2 || !trimmedLine.EndsWith(')'))
        {
            return false;
        }

        block = new MarkdownBlock
        {
            Type = BlockType.Image,
            ImageAltText = trimmedLine[2..altEnd],
            ImageSource = trimmedLine[(altEnd + 2)..^1]
        };

        return true;
    }

    private static bool TryParseTaskList(string trimmedLine, int listLevel, out MarkdownBlock block)
    {
        block = null!;
        if (trimmedLine.Length < 6)
        {
            return false;
        }

        if ((trimmedLine[0] != '-' && trimmedLine[0] != '*') ||
            trimmedLine[1] != ' ' ||
            trimmedLine[2] != '[' ||
            trimmedLine[4] != ']' ||
            trimmedLine[5] != ' ')
        {
            return false;
        }

        var state = trimmedLine[3];
        if (state != ' ' && state != 'x' && state != 'X')
        {
            return false;
        }

        block = new MarkdownBlock
        {
            Type = BlockType.TaskListItem,
            Content = trimmedLine[6..].Trim(),
            IsChecked = state != ' ',
            ListLevel = listLevel
        };

        return true;
    }

    private static bool TryParseOrderedList(string trimmedLine, int listLevel, out MarkdownBlock block)
    {
        block = null!;
        var digitEnd = 0;
        while (digitEnd < trimmedLine.Length && char.IsAsciiDigit(trimmedLine[digitEnd]))
        {
            digitEnd++;
        }

        if (digitEnd == 0 || digitEnd + 1 >= trimmedLine.Length || trimmedLine[digitEnd] != '.' || trimmedLine[digitEnd + 1] != ' ')
        {
            return false;
        }

        if (!int.TryParse(trimmedLine[..digitEnd], out var number))
        {
            return false;
        }

        block = new MarkdownBlock
        {
            Type = BlockType.OrderedListItem,
            Content = trimmedLine[(digitEnd + 2)..].Trim(),
            OrderedNumber = number,
            ListLevel = listLevel
        };

        return true;
    }

    private static bool TryParseBulletList(string trimmedLine, int listLevel, out MarkdownBlock block)
    {
        block = null!;
        if (trimmedLine.Length < 3 || (trimmedLine[0] != '*' && trimmedLine[0] != '-') || trimmedLine[1] != ' ')
        {
            return false;
        }

        block = new MarkdownBlock
        {
            Type = BlockType.BulletListItem,
            Content = trimmedLine[2..].Trim(),
            ListLevel = listLevel
        };

        return true;
    }

    private static bool LooksLikeTableRow(string line)
    {
        return line.Contains('|', StringComparison.Ordinal);
    }

    private static bool IsHorizontalRule(string line)
    {
        var marker = '\0';
        var count = 0;

        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == ' ')
            {
                continue;
            }

            if (marker == '\0')
            {
                marker = ch;
                if (marker != '-' && marker != '*' && marker != '_')
                {
                    return false;
                }
            }
            else if (ch != marker)
            {
                return false;
            }

            count++;
        }

        return count >= 3;
    }

    private static int GetListLevel(string line)
    {
        var spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ')
        {
            spaces++;
        }

        return spaces / 2;
    }

    private static bool IsTableDividerRow(string line)
    {
        var cells = ParseTableCells(line);
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            var trimmed = cell.Trim();
            if (trimmed.Length < 3)
            {
                return false;
            }

            for (var index = 0; index < trimmed.Length; index++)
            {
                var ch = trimmed[index];
                if (ch != '-' && ch != ':')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static List<string> ParseTableCells(string line)
    {
        var trimmedLine = line.Trim();
        var startIndex = trimmedLine.StartsWith('|') ? 1 : 0;
        var endIndex = trimmedLine.EndsWith('|') ? trimmedLine.Length - 1 : trimmedLine.Length;
        var cells = new List<string>();
        var currentCell = new StringBuilder();

        for (var index = startIndex; index < endIndex; index++)
        {
            var ch = trimmedLine[index];
            if (ch == '|')
            {
                cells.Add(currentCell.ToString().Trim());
                currentCell.Clear();
                continue;
            }

            currentCell.Append(ch);
        }

        cells.Add(currentCell.ToString().Trim());
        return cells;
    }

    private static List<MarkdownAlignment> ParseTableAlignments(string dividerRow)
    {
        var cells = ParseTableCells(dividerRow);
        var alignments = new List<MarkdownAlignment>(cells.Count);

        foreach (var cell in cells)
        {
            var trimmed = cell.Trim();
            var startsWithColon = trimmed.StartsWith(':');
            var endsWithColon = trimmed.EndsWith(':');

            if (startsWithColon && endsWithColon)
            {
                alignments.Add(MarkdownAlignment.Center);
            }
            else if (endsWithColon)
            {
                alignments.Add(MarkdownAlignment.Right);
            }
            else
            {
                alignments.Add(MarkdownAlignment.Left);
            }
        }

        return alignments;
    }

    private static void AppendFootnotes(List<MarkdownBlock> blocks, Dictionary<string, string> footnotes)
    {
        if (footnotes.Count == 0)
        {
            return;
        }

        blocks.Add(new MarkdownBlock
        {
            Type = BlockType.HorizontalRule
        });

        blocks.Add(new MarkdownBlock
        {
            Type = BlockType.Header,
            HeaderLevel = 2,
            Content = "Footnotes"
        });

        foreach (var footnote in footnotes)
        {
            blocks.Add(new MarkdownBlock
            {
                Type = BlockType.Footnote,
                FootnoteId = footnote.Key,
                Content = footnote.Value
            });
        }
    }
}
