using MauiMds.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MauiMds.Processors;

public class MdsParser
{
    private static readonly Regex OrderedListPattern = new(@"^(?<number>\d+)\.\s+(?<content>.+)$", RegexOptions.Compiled);
    private static readonly Regex TaskListPattern = new(@"^[-*]\s+\[(?<state>[ xX])\]\s+(?<content>.+)$", RegexOptions.Compiled);
    private static readonly Regex ImagePattern = new(@"^!\[(?<alt>.*?)\]\((?<src>.+?)\)$", RegexOptions.Compiled);
    private static readonly Regex FootnoteDefinitionPattern = new(@"^\[\^(?<id>[^\]]+)\]:\s*(?<content>.*)$", RegexOptions.Compiled);
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

        var frontMatter = ExtractFrontMatter(ref lines);
        if (!string.IsNullOrWhiteSpace(frontMatter))
        {
            blocks.Add(new MarkdownBlock
            {
                Type = BlockType.FrontMatter,
                Content = frontMatter
            });
        }

        var footnotes = ExtractFootnotes(lines);

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
            if (line is null)
            {
                continue;
            }

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
                var alignments = ParseTableAlignments(lines[index + 1]);
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
                    TableRows = rows,
                    TableAlignments = alignments
                });
                continue;
            }

            if (trimmedLine.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph();
                var quoteLines = new List<string>();
                var maxQuoteLevel = 1;

                while (index < lines.Length)
                {
                    var quoteSourceLine = lines[index];
                    if (quoteSourceLine is null)
                    {
                        index++;
                        continue;
                    }

                    var quoteLine = quoteSourceLine.Trim();
                    if (string.IsNullOrWhiteSpace(quoteLine))
                    {
                        quoteLines.Add(string.Empty);
                        index++;
                        continue;
                    }

                    if (!quoteLine.StartsWith(">", StringComparison.Ordinal))
                    {
                        index--;
                        break;
                    }

                    var quoteLevel = 0;
                    var quoteContent = quoteLine;
                    while (quoteContent.StartsWith(">", StringComparison.Ordinal))
                    {
                        quoteLevel++;
                        quoteContent = quoteContent[1..].TrimStart();
                    }

                    maxQuoteLevel = Math.Max(maxQuoteLevel, quoteLevel);
                    quoteLines.Add(quoteContent);
                    index++;
                }

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.BlockQuote,
                    Content = string.Join(Environment.NewLine, quoteLines),
                    QuoteLevel = maxQuoteLevel
                });
                continue;
            }

            if (IsHorizontalRule(trimmedLine))
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.HorizontalRule
                });
                continue;
            }

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
                    HeaderLevel = Math.Min(level, 6),
                    Content = text
                });
                continue;
            }

            var imageMatch = ImagePattern.Match(trimmedLine);
            if (imageMatch.Success)
            {
                FlushParagraph();

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.Image,
                    ImageAltText = imageMatch.Groups["alt"].Value,
                    ImageSource = imageMatch.Groups["src"].Value
                });
                continue;
            }

            var listLevel = GetListLevel(line);

            var taskListMatch = TaskListPattern.Match(trimmedLine);
            if (taskListMatch.Success)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.TaskListItem,
                    Content = taskListMatch.Groups["content"].Value.Trim(),
                    IsChecked = !string.Equals(taskListMatch.Groups["state"].Value, " ", StringComparison.Ordinal),
                    ListLevel = listLevel
                });
                continue;
            }

            var orderedListMatch = OrderedListPattern.Match(trimmedLine);
            if (orderedListMatch.Success)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.OrderedListItem,
                    Content = orderedListMatch.Groups["content"].Value.Trim(),
                    OrderedNumber = int.Parse(orderedListMatch.Groups["number"].Value),
                    ListLevel = listLevel
                });
                continue;
            }

            if (trimmedLine.StartsWith("* ", StringComparison.Ordinal) || trimmedLine.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();

                blocks.Add(new MarkdownBlock
                {
                    Type = BlockType.BulletListItem,
                    Content = trimmedLine[2..].Trim(),
                    ListLevel = listLevel
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
        AppendFootnotes(blocks, footnotes);

        _logger.LogInformation("Completed markdown parse. LineCount: {LineCount}, BlockCount: {BlockCount}", lines.Length, blocks.Count);
        return blocks;
    }

    private static string? ExtractFrontMatter(ref string[] lines)
    {
        if (lines.Length < 3)
        {
            return null;
        }

        var openingDelimiter = lines[0].Trim();
        if (!string.Equals(openingDelimiter, "---", StringComparison.Ordinal) &&
            !string.Equals(openingDelimiter, "+++", StringComparison.Ordinal))
        {
            return null;
        }

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.Equals(lines[index].Trim(), openingDelimiter, StringComparison.Ordinal))
            {
                var frontMatterLines = lines[1..index];
                lines = lines[(index + 1)..];
                return string.Join(Environment.NewLine, frontMatterLines).Trim();
            }
        }

        return null;
    }

    private static bool IsTableHeader(string[] lines, int index)
    {
        return index + 1 < lines.Length
            && lines[index] is not null
            && lines[index + 1] is not null
            && LooksLikeTableRow(lines[index])
            && IsTableDividerRow(lines[index + 1]);
    }

    private static bool LooksLikeTableRow(string line)
    {
        return line.Contains('|', StringComparison.Ordinal);
    }

    private static bool IsHorizontalRule(string line)
    {
        var normalized = line.Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.Length >= 3 &&
               (normalized.All(ch => ch == '-') ||
                normalized.All(ch => ch == '*') ||
                normalized.All(ch => ch == '_'));
    }

    private static int GetListLevel(string line)
    {
        var leadingSpaces = line.TakeWhile(ch => ch == ' ').Count();
        return leadingSpaces / 2;
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

    private static List<MarkdownAlignment> ParseTableAlignments(string dividerRow)
    {
        return ParseTableCells(dividerRow)
            .Select(cell =>
            {
                var trimmed = cell.Trim();
                var startsWithColon = trimmed.StartsWith(':');
                var endsWithColon = trimmed.EndsWith(':');

                if (startsWithColon && endsWithColon)
                {
                    return MarkdownAlignment.Center;
                }

                if (endsWithColon)
                {
                    return MarkdownAlignment.Right;
                }

                return MarkdownAlignment.Left;
            })
            .ToList();
    }

    private static Dictionary<string, string> ExtractFootnotes(string[] lines)
    {
        var footnotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line is null)
            {
                continue;
            }

            var match = FootnoteDefinitionPattern.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var noteLines = new List<string> { match.Groups["content"].Value.Trim() };
            lines[index] = null!;

            while (index + 1 < lines.Length)
            {
                var continuationLine = lines[index + 1];
                if (continuationLine is null)
                {
                    index++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(continuationLine))
                {
                    lines[index + 1] = null!;
                    noteLines.Add(string.Empty);
                    index++;
                    continue;
                }

                if (!continuationLine.StartsWith("    ", StringComparison.Ordinal) &&
                    !continuationLine.StartsWith('\t'))
                {
                    break;
                }

                lines[index + 1] = null!;
                noteLines.Add(continuationLine.Trim());
                index++;
            }

            footnotes[match.Groups["id"].Value] = string.Join(Environment.NewLine, noteLines).Trim();
        }

        return footnotes;
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
