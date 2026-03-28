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
        var blocks = new List<MarkdownBlock>();
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        MarkdownBlock? currentParagraph = null;

        void FlushParagraph()
        {
            if (currentParagraph is null)
            {
                return;
            }

            blocks.Add(currentParagraph);
            currentParagraph = null;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                FlushParagraph();
                continue;
            }

            if (trimmedLine.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();

                var codeLines = new List<string>();
                var codeLanguage = trimmedLine[3..].Trim();

                while (++index < lines.Length)
                {
                    var codeLine = lines[index];
                    if (codeLine.Trim().StartsWith("```", StringComparison.Ordinal))
                    {
                        break;
                    }

                    codeLines.Add(codeLine);
                }

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.CodeBlock,
                    CodeLanguage = codeLanguage,
                    Content = string.Join(Environment.NewLine, codeLines)
                });
                continue;
            }

            if (IsTableHeader(lines, index))
            {
                FlushParagraph();

                var headers = ParseTableCells(lines[index]);
                var rows = new List<List<string>>();
                index += 2;

                while (index < lines.Length)
                {
                    var tableLine = lines[index];
                    if (string.IsNullOrWhiteSpace(tableLine) || !LooksLikeTableRow(tableLine))
                    {
                        index--;
                        break;
                    }

                    rows.Add(ParseTableCells(tableLine));
                    index++;
                }

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.Table,
                    TableHeaders = headers,
                    TableRows = rows
                });
                continue;
            }

            if (trimmedLine.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph();
                var quoteLines = new List<string>();

                while (index < lines.Length)
                {
                    var quoteLine = lines[index].Trim();
                    if (!quoteLine.StartsWith(">", StringComparison.Ordinal))
                    {
                        index--;
                        break;
                    }

                    quoteLines.Add(quoteLine[1..].TrimStart());
                    index++;
                }

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.BlockQuote,
                    Content = string.Join(Environment.NewLine, quoteLines)
                });
                continue;
            }

            // Header (# or ##)
            if (trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                FlushParagraph();

                var level = 0;
                while (level < trimmedLine.Length && trimmedLine[level] == '#')
                {
                    level++;
                }

                var text = trimmedLine[level..].Trim();
                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.Header,
                    HeaderLevel = Math.Min(level, 2),
                    Content = text
                });
                continue;
            }

            // Bullet points (* or -)
            if (trimmedLine.StartsWith("* ", StringComparison.Ordinal) || trimmedLine.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.BulletListItem,
                    Content = trimmedLine[2..].Trim()
                });
                continue;
            }

            if (currentParagraph == null)
            {
                currentParagraph = new MarkdownBlock
                {
                    Type = BlockType.Paragraph,
                    Content = trimmedLine
                };
            }
            else
            {
                currentParagraph.Content += " " + trimmedLine;
            }
        }

        FlushParagraph();

        _logger.LogInformation("Completed markdown parse. LineCount: {LineCount}, BlockCount: {BlockCount}", lines.Length, blocks.Count);
        return blocks;
    }

    private static bool IsTableHeader(string[] lines, int index)
    {
        return index + 1 < lines.Length
            && LooksLikeTableRow(lines[index])
            && IsTableDividerRow(lines[index + 1]);
    }

    private static bool LooksLikeTableRow(string line)
    {
        return line.Contains('|', StringComparison.Ordinal);
    }

    private static bool IsTableDividerRow(string line)
    {
        var cells = ParseTableCells(line);
        if (cells.Count == 0)
        {
            return false;
        }

        return cells.All(cell =>
        {
            var trimmed = cell.Trim();
            return trimmed.Length >= 3 && trimmed.All(ch => ch == '-' || ch == ':');
        });
    }

    private static List<string> ParseTableCells(string line)
    {
        var trimmedLine = line.Trim();
        if (trimmedLine.StartsWith('|'))
        {
            trimmedLine = trimmedLine[1..];
        }

        if (trimmedLine.EndsWith('|'))
        {
            trimmedLine = trimmedLine[..^1];
        }

        return trimmedLine
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
    }
}
