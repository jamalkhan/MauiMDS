using System.Text.RegularExpressions;

namespace MauiMds.Features.Markdown;

public static class MarkdownInlineTokenizer
{
    private static readonly Regex AutolinkRegex = new(
        @"https?://\S+|[\w\.\-]+@[\w\.\-]+\.\w+",
        RegexOptions.Compiled);

    public static IReadOnlyList<InlineToken> Tokenize(string source)
    {
        var tokens = new List<InlineToken>();
        var text = NormalizeInlineHtml(source);
        var index = 0;

        while (index < text.Length)
        {
            if (TryAppendEscapedCharacter(tokens, text, ref index) ||
                TryAppendAngleBracketAutolink(tokens, text, ref index) ||
                TryAppendDelimitedToken(tokens, text, ref index, "**", InlineTokenStyle.Bold) ||
                TryAppendDelimitedToken(tokens, text, ref index, "~~", InlineTokenStyle.Strikethrough) ||
                TryAppendDelimitedToken(tokens, text, ref index, "==", InlineTokenStyle.Highlight) ||
                TryAppendDelimitedToken(tokens, text, ref index, "__", InlineTokenStyle.Underline) ||
                TryAppendDelimitedToken(tokens, text, ref index, "_", InlineTokenStyle.Italic) ||
                TryAppendDelimitedToken(tokens, text, ref index, "*", InlineTokenStyle.Italic) ||
                TryAppendInlineCode(tokens, text, ref index) ||
                TryAppendLink(tokens, text, ref index) ||
                TryAppendFootnoteReference(tokens, text, ref index) ||
                TryAppendSuperscript(tokens, text, ref index) ||
                TryAppendSubscript(tokens, text, ref index))
            {
                continue;
            }

            AppendPlainText(tokens, text, ref index);
        }

        return tokens;
    }

    private static string NormalizeInlineHtml(string source)
    {
        var withTagsNormalized = source
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<strong>", "**", StringComparison.OrdinalIgnoreCase)
            .Replace("</strong>", "**", StringComparison.OrdinalIgnoreCase)
            .Replace("<b>", "**", StringComparison.OrdinalIgnoreCase)
            .Replace("</b>", "**", StringComparison.OrdinalIgnoreCase)
            .Replace("<em>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("</em>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("<i>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("</i>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("<code>", "`", StringComparison.OrdinalIgnoreCase)
            .Replace("</code>", "`", StringComparison.OrdinalIgnoreCase)
            .Replace("<u>", "__", StringComparison.OrdinalIgnoreCase)
            .Replace("</u>", "__", StringComparison.OrdinalIgnoreCase)
            .Replace("<ins>", "__", StringComparison.OrdinalIgnoreCase)
            .Replace("</ins>", "__", StringComparison.OrdinalIgnoreCase)
            .Replace("<mark>", "==", StringComparison.OrdinalIgnoreCase)
            .Replace("</mark>", "==", StringComparison.OrdinalIgnoreCase)
            .Replace("<del>", "~~", StringComparison.OrdinalIgnoreCase)
            .Replace("</del>", "~~", StringComparison.OrdinalIgnoreCase)
            .Replace("<s>", "~~", StringComparison.OrdinalIgnoreCase)
            .Replace("</s>", "~~", StringComparison.OrdinalIgnoreCase)
            .Replace("<sub>", "~", StringComparison.OrdinalIgnoreCase)
            .Replace("</sub>", "~", StringComparison.OrdinalIgnoreCase)
            .Replace("<sup>", "^", StringComparison.OrdinalIgnoreCase)
            .Replace("</sup>", "^", StringComparison.OrdinalIgnoreCase);
        return System.Net.WebUtility.HtmlDecode(withTagsNormalized);
    }

    private static bool TryAppendEscapedCharacter(List<InlineToken> tokens, string text, ref int index)
    {
        if (text[index] != '\\' || index + 1 >= text.Length)
        {
            return false;
        }

        tokens.Add(new InlineToken(text[index + 1].ToString(), InlineTokenStyle.Plain));
        index += 2;
        return true;
    }

    private static bool TryAppendAngleBracketAutolink(List<InlineToken> tokens, string text, ref int index)
    {
        if (text[index] != '<')
        {
            return false;
        }

        var closingIndex = text.IndexOf('>', index + 1);
        if (closingIndex < 0)
        {
            return false;
        }

        var inner = text.Substring(index + 1, closingIndex - index - 1);
        if (inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(new InlineToken(inner, InlineTokenStyle.Link, inner));
            index = closingIndex + 1;
            return true;
        }

        if (inner.Contains('@') && !inner.Contains(' ') && inner.Contains('.'))
        {
            tokens.Add(new InlineToken(inner, InlineTokenStyle.Link, $"mailto:{inner}"));
            index = closingIndex + 1;
            return true;
        }

        return false;
    }

    private static bool TryAppendDelimitedToken(List<InlineToken> tokens, string text, ref int index, string delimiter, InlineTokenStyle style)
    {
        if (!text.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
        {
            return false;
        }

        var closingIndex = text.IndexOf(delimiter, index + delimiter.Length, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return false;
        }

        var innerText = text.Substring(index + delimiter.Length, closingIndex - index - delimiter.Length);
        tokens.Add(new InlineToken(innerText, style));
        index = closingIndex + delimiter.Length;
        return true;
    }

    private static bool TryAppendInlineCode(List<InlineToken> tokens, string text, ref int index)
    {
        if (text[index] != '`')
        {
            return false;
        }

        var closingIndex = text.IndexOf('`', index + 1);
        if (closingIndex < 0)
        {
            return false;
        }

        tokens.Add(new InlineToken(text.Substring(index + 1, closingIndex - index - 1), InlineTokenStyle.InlineCode));
        index = closingIndex + 1;
        return true;
    }

    private static bool TryAppendLink(List<InlineToken> tokens, string text, ref int index)
    {
        if (text[index] != '[')
        {
            return false;
        }

        var labelEnd = text.IndexOf("](", index, StringComparison.Ordinal);
        if (labelEnd < 0)
        {
            return false;
        }

        var urlEnd = text.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
        {
            return false;
        }

        var label = text.Substring(index + 1, labelEnd - index - 1);
        var urlPart = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2);

        // Strip optional title from URL part: [text](url "title")
        var url = urlPart;
        if (urlPart.Length > 2)
        {
            var lastChar = urlPart[^1];
            if (lastChar == '"' || lastChar == '\'')
            {
                var titleOpen = urlPart.LastIndexOf(lastChar, urlPart.Length - 2);
                if (titleOpen > 0 && urlPart[titleOpen - 1] == ' ')
                {
                    url = urlPart[..(titleOpen - 1)].Trim();
                }
            }
        }

        tokens.Add(new InlineToken(label, InlineTokenStyle.Link, url));
        index = urlEnd + 1;
        return true;
    }

    private static bool TryAppendFootnoteReference(List<InlineToken> tokens, string text, ref int index)
    {
        if (!text.AsSpan(index).StartsWith("[^", StringComparison.Ordinal))
        {
            return false;
        }

        var closingIndex = text.IndexOf(']', index + 2);
        if (closingIndex < 0)
        {
            return false;
        }

        var reference = text.Substring(index + 2, closingIndex - index - 2);
        tokens.Add(new InlineToken($"[{reference}]", InlineTokenStyle.FootnoteReference));
        index = closingIndex + 1;
        return true;
    }

    private static bool TryAppendSuperscript(List<InlineToken> tokens, string text, ref int index)
    {
        if (text[index] != '^')
        {
            return false;
        }

        var closingIndex = text.IndexOf('^', index + 1);
        if (closingIndex < 0 || closingIndex == index + 1)
        {
            return false;
        }

        tokens.Add(new InlineToken(text.Substring(index + 1, closingIndex - index - 1), InlineTokenStyle.Superscript));
        index = closingIndex + 1;
        return true;
    }

    private static bool TryAppendSubscript(List<InlineToken> tokens, string text, ref int index)
    {
        if (text[index] != '~')
        {
            return false;
        }

        // Only match single tilde (double tilde is handled by ~~ strikethrough before this)
        var closingIndex = text.IndexOf('~', index + 1);
        if (closingIndex < 0 || closingIndex == index + 1)
        {
            return false;
        }

        tokens.Add(new InlineToken(text.Substring(index + 1, closingIndex - index - 1), InlineTokenStyle.Subscript));
        index = closingIndex + 1;
        return true;
    }

    private static void AppendPlainText(List<InlineToken> tokens, string text, ref int index)
    {
        var nextSpecialIndex = FindNextSpecialIndex(text, index);

        if (nextSpecialIndex == index)
        {
            tokens.Add(new InlineToken(text[index].ToString(), InlineTokenStyle.Plain));
            index++;
            return;
        }

        var chunk = nextSpecialIndex < 0
            ? text[index..]
            : text.Substring(index, nextSpecialIndex - index);

        var chunkIndex = 0;
        foreach (Match match in AutolinkRegex.Matches(chunk))
        {
            if (match.Index > chunkIndex)
            {
                tokens.Add(new InlineToken(chunk.Substring(chunkIndex, match.Index - chunkIndex), InlineTokenStyle.Plain));
            }

            var target = match.Value.Contains('@') && !match.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"mailto:{match.Value}"
                : match.Value;
            tokens.Add(new InlineToken(match.Value, InlineTokenStyle.Link, target));
            chunkIndex = match.Index + match.Length;
        }

        if (chunkIndex < chunk.Length)
        {
            tokens.Add(new InlineToken(chunk[chunkIndex..], InlineTokenStyle.Plain));
        }

        index = nextSpecialIndex < 0 ? text.Length : nextSpecialIndex;
    }

    private static int FindNextSpecialIndex(string text, int startIndex)
    {
        var specials = new[] { '\\', '*', '`', '[', '~', '=', '^', '_', '<' };
        var nextIndexes = specials
            .Select(ch => text.IndexOf(ch, startIndex))
            .Where(idx => idx >= 0)
            .ToList();

        return nextIndexes.Count == 0 ? -1 : nextIndexes.Min();
    }
}

public readonly record struct InlineToken(string Text, InlineTokenStyle Style, string? Target = null);

public enum InlineTokenStyle
{
    Plain,
    Bold,
    Italic,
    Strikethrough,
    Underline,
    Highlight,
    Superscript,
    Subscript,
    InlineCode,
    Link,
    FootnoteReference
}
