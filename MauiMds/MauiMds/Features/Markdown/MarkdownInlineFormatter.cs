using System.Text.RegularExpressions;

namespace MauiMds.Features.Markdown;

public sealed class MarkdownInlineFormatter
{
    public bool RequiresFormattedText(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        return source.Contains('\\', StringComparison.Ordinal) ||
               source.Contains('*', StringComparison.Ordinal) ||
               source.Contains('`', StringComparison.Ordinal) ||
               source.Contains('[', StringComparison.Ordinal) ||
               source.Contains('~', StringComparison.Ordinal) ||
               source.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
               source.Contains('@', StringComparison.Ordinal) ||
               source.Contains('<', StringComparison.Ordinal);
    }

    public FormattedString BuildFormattedText(string source, double fontSize)
    {
        var formatted = new FormattedString();
        var text = NormalizeInlineHtml(source);
        var index = 0;

        while (index < text.Length)
        {
            if (TryAppendEscapedCharacter(formatted, text, ref index, fontSize) ||
                TryAppendDelimitedSpan(formatted, text, ref index, "**", fontSize, span => span.FontAttributes = FontAttributes.Bold) ||
                TryAppendDelimitedSpan(formatted, text, ref index, "~~", fontSize, span => span.TextDecorations = TextDecorations.Strikethrough) ||
                TryAppendDelimitedSpan(formatted, text, ref index, "*", fontSize, span => span.FontAttributes = FontAttributes.Italic) ||
                TryAppendInlineCode(formatted, text, ref index, fontSize) ||
                TryAppendLink(formatted, text, ref index, fontSize) ||
                TryAppendFootnoteReference(formatted, text, ref index, fontSize))
            {
                continue;
            }

            AppendPlainText(formatted, text, ref index, fontSize);
        }

        return formatted;
    }

    public FormattedString BuildCodeFormattedText(string code, string language)
    {
        var formatted = new FormattedString();
        var keywords = GetCodeKeywords(language);
        var tokenPattern = "(\"[^\"]*\"|'[^']*'|//.*?$|#.*?$|\\b\\d+\\b|\\b[A-Za-z_][A-Za-z0-9_]*\\b)";

        foreach (var line in code.Split(Environment.NewLine))
        {
            var lineIndex = 0;
            foreach (Match match in Regex.Matches(line, tokenPattern, RegexOptions.Multiline))
            {
                if (match.Index > lineIndex)
                {
                    formatted.Spans.Add(CreateCodeSpan(line.Substring(lineIndex, match.Index - lineIndex), null));
                }

                formatted.Spans.Add(CreateCodeSpan(match.Value, keywords.Contains(match.Value) ? "keyword" : ClassifyCodeToken(match.Value)));
                lineIndex = match.Index + match.Length;
            }

            if (lineIndex < line.Length)
            {
                formatted.Spans.Add(CreateCodeSpan(line[lineIndex..], null));
            }

            formatted.Spans.Add(CreateCodeSpan(Environment.NewLine, null));
        }

        return formatted;
    }

    public ImageSource? ResolveImageSource(string source, string sourceFilePath)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
        {
            return ImageSource.FromUri(absoluteUri);
        }

        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            var fileDirectory = System.IO.Path.GetDirectoryName(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(fileDirectory))
            {
                var candidatePath = System.IO.Path.Combine(fileDirectory, source);
                if (File.Exists(candidatePath))
                {
                    return ImageSource.FromFile(candidatePath);
                }
            }
        }

        return File.Exists(source) ? ImageSource.FromFile(source) : null;
    }

    private string NormalizeInlineHtml(string source)
    {
        return source
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
            .Replace("</code>", "`", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAppendEscapedCharacter(FormattedString formatted, string text, ref int index, double fontSize)
    {
        if (text[index] != '\\' || index + 1 >= text.Length)
        {
            return false;
        }

        formatted.Spans.Add(CreateSpan(text[index + 1].ToString(), fontSize));
        index += 2;
        return true;
    }

    private bool TryAppendDelimitedSpan(FormattedString formatted, string text, ref int index, string delimiter, double fontSize, Action<Span> style)
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
        var span = CreateSpan(innerText, fontSize);
        style(span);
        formatted.Spans.Add(span);
        index = closingIndex + delimiter.Length;
        return true;
    }

    private bool TryAppendInlineCode(FormattedString formatted, string text, ref int index, double fontSize)
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

        var span = CreateSpan(text.Substring(index + 1, closingIndex - index - 1), fontSize - 1);
        span.FontFamily = "Courier New";
        span.BackgroundColor = Color.FromArgb("#E8E1D3");
        formatted.Spans.Add(span);
        index = closingIndex + 1;
        return true;
    }

    private bool TryAppendLink(FormattedString formatted, string text, ref int index, double fontSize)
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
        var url = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2);
        formatted.Spans.Add(CreateLinkSpan(label, url, fontSize));
        index = urlEnd + 1;
        return true;
    }

    private bool TryAppendFootnoteReference(FormattedString formatted, string text, ref int index, double fontSize)
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
        var span = CreateSpan($"[{reference}]", Math.Max(10, fontSize - 4));
        span.FontAttributes = FontAttributes.Bold;
        span.TextColor = Color.FromArgb("#8D5A2B");
        formatted.Spans.Add(span);
        index = closingIndex + 1;
        return true;
    }

    private void AppendPlainText(FormattedString formatted, string text, ref int index, double fontSize)
    {
        var nextSpecialIndex = FindNextSpecialIndex(text, index);

        if (nextSpecialIndex == index)
        {
            formatted.Spans.Add(CreateSpan(text[index].ToString(), fontSize));
            index++;
            return;
        }

        var chunk = nextSpecialIndex < 0
            ? text[index..]
            : text.Substring(index, nextSpecialIndex - index);

        var chunkIndex = 0;
        foreach (Match match in Regex.Matches(chunk, @"https?://\S+|[\w\.\-]+@[\w\.\-]+\.\w+"))
        {
            if (match.Index > chunkIndex)
            {
                formatted.Spans.Add(CreateSpan(chunk.Substring(chunkIndex, match.Index - chunkIndex), fontSize));
            }

            var target = match.Value.Contains('@') && !match.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"mailto:{match.Value}"
                : match.Value;
            formatted.Spans.Add(CreateLinkSpan(match.Value, target, fontSize));
            chunkIndex = match.Index + match.Length;
        }

        if (chunkIndex < chunk.Length)
        {
            formatted.Spans.Add(CreateSpan(chunk[chunkIndex..], fontSize));
        }

        index = nextSpecialIndex < 0 ? text.Length : nextSpecialIndex;
    }

    private static int FindNextSpecialIndex(string text, int startIndex)
    {
        var specials = new[] { '\\', '*', '`', '[', '~' };
        var nextIndexes = specials
            .Select(ch => text.IndexOf(ch, startIndex))
            .Where(idx => idx >= 0)
            .ToList();

        return nextIndexes.Count == 0 ? -1 : nextIndexes.Min();
    }

    private Span CreateSpan(string text, double fontSize)
    {
        var span = new Span
        {
            Text = text,
            FontSize = fontSize
        };
        span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#161616"), Color.FromArgb("#F3EDE2"));
        return span;
    }

    private Span CreateLinkSpan(string label, string url, double fontSize)
    {
        var span = CreateSpan(label, fontSize);
        span.TextColor = Color.FromArgb("#2B6CB0");
        span.TextDecorations = TextDecorations.Underline;

        if (DeviceInfo.Platform == DevicePlatform.MacCatalyst)
        {
            return span;
        }

        span.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Launcher.OpenAsync(url))
        });
        return span;
    }

    private static HashSet<string> GetCodeKeywords(string language)
    {
        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "csharp" or "cs" => ["public", "private", "class", "void", "string", "int", "var", "return", "if", "else", "foreach", "async", "await", "new", "using", "namespace"],
            "javascript" or "js" or "typescript" or "ts" => ["function", "const", "let", "var", "return", "if", "else", "class", "import", "export", "async", "await", "new"],
            "json" => [],
            "sql" => ["select", "from", "where", "order", "by", "group", "insert", "update", "delete", "join", "left", "right"],
            "bash" or "sh" => ["if", "then", "fi", "for", "do", "done", "echo", "export"],
            "xml" or "html" => [],
            _ => ["true", "false", "null"]
        };
    }

    private Span CreateCodeSpan(string text, string? tokenType)
    {
        var span = new Span
        {
            Text = text,
            FontFamily = "Courier New",
            FontSize = 15
        };

        span.TextColor = tokenType switch
        {
            "keyword" => Color.FromArgb("#8B3F96"),
            "string" => Color.FromArgb("#2F855A"),
            "comment" => Color.FromArgb("#718096"),
            "number" => Color.FromArgb("#B7791F"),
            _ => Color.FromArgb("#1E1E1E")
        };

        return span;
    }

    private static string? ClassifyCodeToken(string value)
    {
        if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith('#'))
        {
            return "comment";
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return "string";
        }

        if (value.All(char.IsDigit))
        {
            return "number";
        }

        return null;
    }
}
