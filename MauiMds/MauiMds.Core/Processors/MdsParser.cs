using System.Text;
using System.Text.RegularExpressions;
using MauiMds.Models;
using Microsoft.Extensions.Logging;

namespace MauiMds.Processors;

public class MdsParser
{
    private static readonly Regex ReferenceLinkDefinitionPattern = new(
        @"^\[([^\]]+)\]:\s+(\S+)(?:\s+""([^""]*)"")?$",
        RegexOptions.Compiled);

    private static readonly Regex ReferenceLinkUsagePattern = new(
        @"\[([^\]]+)\]\[([^\]]*)\]",
        RegexOptions.Compiled);

    private static readonly string[] AdmonitionTypes =
        ["NOTE", "TIP", "WARNING", "IMPORTANT", "CAUTION", "INFO", "DANGER", "SUCCESS", "BUG", "EXAMPLE", "QUESTION", "ABSTRACT", "TLDR"];

    private readonly ILogger<MdsParser> _logger;

    public MdsParser(ILogger<MdsParser> logger)
    {
        _logger = logger;
    }

    public virtual List<MarkdownBlock> Parse(string content)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var codeBlockCount = 0;
        var tableCount = 0;
        var blockQuoteCount = 0;
        var admonitionCount = 0;
        var headerCount = 0;
        var imageCount = 0;
        var taskListCount = 0;
        var orderedListCount = 0;
        var bulletListCount = 0;
        var horizontalRuleCount = 0;
        var footnoteDefinitionCount = 0;
        var definitionCount = 0;

        _logger.LogDebug("Starting markdown parse. CharacterCount: {CharacterCount}", content.Length);

        var normalizedContent = NormalizeNewLines(content);
        var lines = normalizedContent.Split('\n');

        var referenceLinks = CollectReferenceLinks(lines);
        var blocks = new List<MarkdownBlock>(Math.Max(8, lines.Length / 2));
        var footnotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentParagraph = new StringBuilder();
        var pendingHardBreak = false;

        var index = 0;
        if (TryExtractFrontMatter(lines, ref index, out var frontMatter))
        {
            blocks.Add(new MarkdownBlock
            {
                Type = BlockType.FrontMatter,
                Content = frontMatter
            });
            _logger.LogTrace("Detected front matter block. Length: {Length}", frontMatter.Length);
        }

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmedLine = line.Trim();

            if (trimmedLine.Length == 0)
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                index++;
                continue;
            }

            if (IsReferenceLinkDefinition(trimmedLine))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                index++;
                continue;
            }

            if (TryExtractFootnote(lines, ref index, trimmedLine, footnotes))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                footnoteDefinitionCount++;
                continue;
            }

            if (IsCodeFence(trimmedLine))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(ParseCodeBlock(lines, ref index, trimmedLine));
                codeBlockCount++;
                continue;
            }

            if (IsTableHeader(lines, index))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(ParseTable(lines, ref index));
                tableCount++;
                continue;
            }

            if (trimmedLine[0] == '>')
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                var quoteBlock = ParseBlockQuote(lines, ref index);
                if (quoteBlock.Type == BlockType.Admonition)
                {
                    admonitionCount++;
                }
                else
                {
                    blockQuoteCount++;
                }
                blocks.Add(quoteBlock);
                continue;
            }

            if (IsHorizontalRule(trimmedLine))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(new MarkdownBlock { Type = BlockType.HorizontalRule });
                horizontalRuleCount++;
                index++;
                continue;
            }

            if (TryParseHeader(trimmedLine, out var headerBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(headerBlock);
                headerCount++;
                index++;
                continue;
            }

            if (TryParseImage(trimmedLine, out var imageBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(imageBlock);
                imageCount++;
                index++;
                continue;
            }

            if (TryParseDefinitionDetail(trimmedLine, out var defDetailBlock))
            {
                if (currentParagraph.Length > 0)
                {
                    blocks.Add(new MarkdownBlock
                    {
                        Type = BlockType.DefinitionTerm,
                        Content = currentParagraph.ToString()
                    });
                    currentParagraph.Clear();
                    pendingHardBreak = false;
                }
                blocks.Add(defDetailBlock);
                definitionCount++;
                index++;
                continue;
            }

            var listLevel = GetListLevel(line);

            if (TryParseTaskList(trimmedLine, listLevel, out var taskBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(taskBlock);
                taskListCount++;
                index++;
                continue;
            }

            if (TryParseOrderedList(trimmedLine, listLevel, out var orderedBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(orderedBlock);
                orderedListCount++;
                index++;
                continue;
            }

            if (TryParseBulletList(trimmedLine, listLevel, out var bulletBlock))
            {
                FlushParagraph(blocks, currentParagraph);
                pendingHardBreak = false;
                blocks.Add(bulletBlock);
                bulletListCount++;
                index++;
                continue;
            }

            var hasBackslashBreak = trimmedLine.EndsWith('\\');
            var thisLineHardBreak = HasTrailingTwoSpaces(line) || hasBackslashBreak;
            var lineContent = hasBackslashBreak ? trimmedLine[..^1].TrimEnd() : trimmedLine;
            AppendParagraphLine(currentParagraph, lineContent, pendingHardBreak);
            pendingHardBreak = thisLineHardBreak;
            index++;
        }

        FlushParagraph(blocks, currentParagraph);
        AppendFootnotes(blocks, footnotes);

        if (referenceLinks.Count > 0)
        {
            ResolveReferenceLinksInBlocks(blocks, referenceLinks);
        }

        _logger.LogDebug(
            "Completed markdown parse. LineCount: {LineCount}, BlockCount: {BlockCount}, Paragraphs: {ParagraphCount}, Headers: {HeaderCount}, BulletItems: {BulletListCount}, OrderedItems: {OrderedListCount}, TaskItems: {TaskListCount}, BlockQuotes: {BlockQuoteCount}, Admonitions: {AdmonitionCount}, Tables: {TableCount}, CodeBlocks: {CodeBlockCount}, Images: {ImageCount}, HorizontalRules: {HorizontalRuleCount}, FootnoteDefinitions: {FootnoteDefinitionCount}, FootnoteBlocks: {FootnoteBlockCount}, Definitions: {DefinitionCount}, ElapsedMs: {ElapsedMs}",
            lines.Length,
            blocks.Count,
            blocks.Count(block => block.Type == BlockType.Paragraph),
            headerCount,
            bulletListCount,
            orderedListCount,
            taskListCount,
            blockQuoteCount,
            admonitionCount,
            tableCount,
            codeBlockCount,
            imageCount,
            horizontalRuleCount,
            footnoteDefinitionCount,
            blocks.Count(block => block.Type == BlockType.Footnote),
            definitionCount,
            stopwatch.ElapsedMilliseconds);
        return blocks;
    }

    private static string NormalizeNewLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static Dictionary<string, (string Url, string Title)> CollectReferenceLinks(string[] lines)
    {
        var refs = new Dictionary<string, (string Url, string Title)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var match = ReferenceLinkDefinitionPattern.Match(trimmed);
            if (match.Success)
            {
                refs[match.Groups[1].Value] = (match.Groups[2].Value, match.Groups[3].Value);
            }
        }
        return refs;
    }

    private static bool IsReferenceLinkDefinition(string trimmedLine)
    {
        return ReferenceLinkDefinitionPattern.IsMatch(trimmedLine);
    }

    private static void ResolveReferenceLinksInBlocks(List<MarkdownBlock> blocks, Dictionary<string, (string Url, string Title)> refs)
    {
        foreach (var block in blocks)
        {
            if (!string.IsNullOrEmpty(block.Content))
            {
                block.Content = ResolveReferenceLinksInText(block.Content, refs);
            }
            if (block.Children.Count > 0)
            {
                ResolveReferenceLinksInBlocks(block.Children, refs);
            }
        }
    }

    private static string ResolveReferenceLinksInText(string text, Dictionary<string, (string Url, string Title)> refs)
    {
        return ReferenceLinkUsagePattern.Replace(text, match =>
        {
            var label = match.Groups[1].Value;
            var key = match.Groups[2].Value;
            if (string.IsNullOrEmpty(key))
            {
                key = label;
            }
            if (refs.TryGetValue(key, out var refData))
            {
                return string.IsNullOrEmpty(refData.Title)
                    ? $"[{label}]({refData.Url})"
                    : $"[{label}]({refData.Url} \"{refData.Title}\")";
            }
            return match.Value;
        });
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

    private static void AppendParagraphLine(StringBuilder paragraphBuilder, string line, bool precededByHardBreak)
    {
        if (paragraphBuilder.Length > 0)
        {
            paragraphBuilder.Append(precededByHardBreak ? '\n' : ' ');
        }

        paragraphBuilder.Append(line);
    }

    private static bool HasTrailingTwoSpaces(string line)
    {
        return line.Length >= 2 && line[^1] == ' ' && line[^2] == ' ';
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
        var innerLines = new List<string>();
        var maxQuoteLevel = 1;

        while (index < lines.Length)
        {
            var sourceLine = lines[index];
            var trimmedLine = sourceLine.Trim();

            if (trimmedLine.Length == 0)
            {
                innerLines.Add(string.Empty);
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
            innerLines.Add(trimmedLine[contentStart..]);
            index++;
        }

        // Check for admonition pattern on the first non-empty inner line
        var firstContent = innerLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        if (firstContent.StartsWith("[!", StringComparison.Ordinal))
        {
            var closeBracket = firstContent.IndexOf(']');
            if (closeBracket > 2)
            {
                var candidate = firstContent[2..closeBracket].ToUpperInvariant();
                if (Array.IndexOf(AdmonitionTypes, candidate) >= 0)
                {
                    var titleSuffix = firstContent[(closeBracket + 1)..].Trim();
                    var contentLines = innerLines
                        .SkipWhile(l => string.IsNullOrWhiteSpace(l) || l.StartsWith("[!", StringComparison.Ordinal))
                        .ToList();
                    var admonitionContent = string.Join("\n", contentLines).Trim();

                    return new MarkdownBlock
                    {
                        Type = BlockType.Admonition,
                        AdmonitionType = candidate,
                        AdmonitionTitle = titleSuffix,
                        Content = admonitionContent
                    };
                }
            }
        }

        // Build content string and check for nested block elements
        var contentBuilder = new StringBuilder();
        foreach (var innerLine in innerLines)
        {
            if (contentBuilder.Length > 0)
            {
                contentBuilder.Append(Environment.NewLine);
            }
            contentBuilder.Append(innerLine);
        }

        var rawContent = contentBuilder.ToString();

        // Parse nested blocks (tables, headers, etc.) within the blockquote
        var children = ParseInnerBlocks(innerLines.ToArray());
        var hasStructuredChildren = children.Any(c => c.Type == BlockType.Table || c.Type == BlockType.Header || c.Type == BlockType.CodeBlock);

        return new MarkdownBlock
        {
            Type = BlockType.BlockQuote,
            Content = rawContent.Trim(),
            QuoteLevel = maxQuoteLevel,
            Children = hasStructuredChildren ? children : []
        };
    }

    private static List<MarkdownBlock> ParseInnerBlocks(string[] innerLines)
    {
        var blocks = new List<MarkdownBlock>();
        var paragraphBuilder = new StringBuilder();
        var index = 0;

        while (index < innerLines.Length)
        {
            var line = innerLines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph(blocks, paragraphBuilder);
                index++;
                continue;
            }

            if (IsCodeFence(trimmed))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(ParseCodeBlock(innerLines, ref index, trimmed));
                continue;
            }

            if (IsTableHeader(innerLines, index))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(ParseTable(innerLines, ref index));
                continue;
            }

            if (TryParseHeader(trimmed, out var headerBlock))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(headerBlock);
                index++;
                continue;
            }

            if (TryParseImage(trimmed, out var imageBlock))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(imageBlock);
                index++;
                continue;
            }

            var listLevel = GetListLevel(line);
            if (TryParseTaskList(trimmed, listLevel, out var taskBlock))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(taskBlock);
                index++;
                continue;
            }
            if (TryParseOrderedList(trimmed, listLevel, out var orderedBlock))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(orderedBlock);
                index++;
                continue;
            }
            if (TryParseBulletList(trimmed, listLevel, out var bulletBlock))
            {
                FlushParagraph(blocks, paragraphBuilder);
                blocks.Add(bulletBlock);
                index++;
                continue;
            }

            AppendParagraphLine(paragraphBuilder, trimmed, precededByHardBreak: false);
            index++;
        }

        FlushParagraph(blocks, paragraphBuilder);
        return blocks;
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

        var altText = trimmedLine[2..altEnd];
        var urlPart = trimmedLine[(altEnd + 2)..^1].Trim();
        var title = string.Empty;

        // Parse optional title: ![alt](url "title") or ![alt](url 'title')
        if (urlPart.Length > 2)
        {
            var lastChar = urlPart[^1];
            if (lastChar == '"' || lastChar == '\'')
            {
                var titleOpen = urlPart.LastIndexOf(lastChar, urlPart.Length - 2);
                if (titleOpen > 0 && urlPart[titleOpen - 1] == ' ')
                {
                    title = urlPart[(titleOpen + 1)..^1];
                    urlPart = urlPart[..(titleOpen - 1)].Trim();
                }
            }
        }

        block = new MarkdownBlock
        {
            Type = BlockType.Image,
            ImageAltText = altText,
            ImageSource = urlPart,
            ImageTitle = title
        };

        return true;
    }

    private static bool TryParseDefinitionDetail(string trimmedLine, out MarkdownBlock block)
    {
        block = null!;
        if (trimmedLine.Length < 3 || trimmedLine[0] != ':' || trimmedLine[1] != ' ')
        {
            return false;
        }

        block = new MarkdownBlock
        {
            Type = BlockType.DefinitionDetail,
            Content = trimmedLine[2..].Trim()
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
