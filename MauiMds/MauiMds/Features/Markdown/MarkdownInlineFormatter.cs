using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MauiMds.Features.Markdown;

public sealed class MarkdownInlineFormatter
{
    private static readonly Regex AutolinkRegex = new(
        @"https?://\S+|[\w\.\-]+@[\w\.\-]+\.\w+",
        RegexOptions.Compiled);

    private static readonly Regex CodeTokenRegex = new(
        "(\"[^\"]*\"|'[^']*'|//.*?$|#.*?$|\\b\\d+\\b|\\b[A-Za-z_][A-Za-z0-9_]*\\b)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, bool> _requiresFormattedTextCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<InlineToken>> _inlineTokenCache = new(StringComparer.Ordinal);
    private readonly Dictionary<CodeCacheKey, IReadOnlyList<CodeToken>> _codeTokenCache = [];
    private ILogger<MarkdownInlineFormatter>? _logger;

    public void AttachLogger(ILogger<MarkdownInlineFormatter>? logger)
    {
        _logger = logger;
    }

    public bool RequiresFormattedText(string source)
    {
        lock (_cacheLock)
        {
            if (_requiresFormattedTextCache.TryGetValue(source, out var cached))
            {
                _logger?.LogTrace("Inline formatter reused formatted-text requirement cache. Length: {Length}, RequiresFormattedText: {RequiresFormattedText}", source.Length, cached);
                return cached;
            }
        }

        var requiresFormattedText = !string.IsNullOrEmpty(source) &&
                                    (source.Contains('\\', StringComparison.Ordinal) ||
                                     source.Contains('*', StringComparison.Ordinal) ||
                                     source.Contains('`', StringComparison.Ordinal) ||
                                     source.Contains('[', StringComparison.Ordinal) ||
                                     source.Contains('~', StringComparison.Ordinal) ||
                                     source.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                                     source.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                                     source.Contains('@', StringComparison.Ordinal) ||
                                     source.Contains('<', StringComparison.Ordinal));

        lock (_cacheLock)
        {
            _requiresFormattedTextCache[source] = requiresFormattedText;
        }

        _logger?.LogTrace("Inline formatter computed formatted-text requirement. Length: {Length}, RequiresFormattedText: {RequiresFormattedText}", source.Length, requiresFormattedText);

        return requiresFormattedText;
    }

    public FormattedString BuildFormattedText(string source, double fontSize)
    {
        var tokens = GetInlineTokens(source);
        var formatted = new FormattedString();

        foreach (var token in tokens)
        {
            formatted.Spans.Add(CreateInlineSpan(token, fontSize));
        }

        return formatted;
    }

    public FormattedString BuildCodeFormattedText(string code, string language)
    {
        var tokens = GetCodeTokens(code, language);
        var formatted = new FormattedString();

        foreach (var token in tokens)
        {
            formatted.Spans.Add(CreateCodeSpan(token.Text, token.TokenType));
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

    private IReadOnlyList<InlineToken> GetInlineTokens(string source)
    {
        lock (_cacheLock)
        {
            if (_inlineTokenCache.TryGetValue(source, out var cached))
            {
                _logger?.LogTrace("Inline formatter inline-token cache hit. Length: {Length}, TokenCount: {TokenCount}", source.Length, cached.Count);
                return cached;
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tokens = TokenizeInline(source);

        lock (_cacheLock)
        {
            _inlineTokenCache[source] = tokens;
        }

        _logger?.LogDebug("Inline formatter inline-token cache miss. Length: {Length}, TokenCount: {TokenCount}, ElapsedMs: {ElapsedMs}", source.Length, tokens.Count, stopwatch.ElapsedMilliseconds);

        return tokens;
    }

    private IReadOnlyList<CodeToken> GetCodeTokens(string code, string language)
    {
        var key = new CodeCacheKey(code, language);

        lock (_cacheLock)
        {
            if (_codeTokenCache.TryGetValue(key, out var cached))
            {
                _logger?.LogTrace("Inline formatter code-token cache hit. Length: {Length}, Language: {Language}, TokenCount: {TokenCount}", code.Length, language, cached.Count);
                return cached;
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tokens = TokenizeCode(code, language);

        lock (_cacheLock)
        {
            _codeTokenCache[key] = tokens;
        }

        _logger?.LogDebug("Inline formatter code-token cache miss. Length: {Length}, Language: {Language}, TokenCount: {TokenCount}, ElapsedMs: {ElapsedMs}", code.Length, language, tokens.Count, stopwatch.ElapsedMilliseconds);

        return tokens;
    }

    private List<InlineToken> TokenizeInline(string source)
    {
        var tokens = new List<InlineToken>();
        var text = NormalizeInlineHtml(source);
        var index = 0;

        while (index < text.Length)
        {
            if (TryAppendEscapedCharacter(tokens, text, ref index) ||
                TryAppendDelimitedToken(tokens, text, ref index, "**", InlineTokenStyle.Bold) ||
                TryAppendDelimitedToken(tokens, text, ref index, "~~", InlineTokenStyle.Strikethrough) ||
                TryAppendDelimitedToken(tokens, text, ref index, "*", InlineTokenStyle.Italic) ||
                TryAppendInlineCode(tokens, text, ref index) ||
                TryAppendLink(tokens, text, ref index) ||
                TryAppendFootnoteReference(tokens, text, ref index))
            {
                continue;
            }

            AppendPlainText(tokens, text, ref index);
        }

        return tokens;
    }

    private List<CodeToken> TokenizeCode(string code, string language)
    {
        var tokens = new List<CodeToken>();
        var keywords = GetCodeKeywords(language);

        foreach (var line in code.Split(Environment.NewLine))
        {
            var lineIndex = 0;

            foreach (Match match in CodeTokenRegex.Matches(line))
            {
                if (match.Index > lineIndex)
                {
                    tokens.Add(new CodeToken(line.Substring(lineIndex, match.Index - lineIndex), null));
                }

                var tokenType = keywords.Contains(match.Value) ? "keyword" : ClassifyCodeToken(match.Value);
                tokens.Add(new CodeToken(match.Value, tokenType));
                lineIndex = match.Index + match.Length;
            }

            if (lineIndex < line.Length)
            {
                tokens.Add(new CodeToken(line[lineIndex..], null));
            }

            tokens.Add(new CodeToken(Environment.NewLine, null));
        }

        return tokens;
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
        var url = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2);
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
        var specials = new[] { '\\', '*', '`', '[', '~' };
        var nextIndexes = specials
            .Select(ch => text.IndexOf(ch, startIndex))
            .Where(idx => idx >= 0)
            .ToList();

        return nextIndexes.Count == 0 ? -1 : nextIndexes.Min();
    }

    private Span CreateInlineSpan(InlineToken token, double fontSize)
    {
        var span = token.Style switch
        {
            InlineTokenStyle.Link => CreateLinkSpan(token.Text, token.Target ?? token.Text, fontSize),
            InlineTokenStyle.InlineCode => CreateInlineCodeSpan(token.Text, fontSize),
            _ => CreateSpan(token.Text, fontSize)
        };

        switch (token.Style)
        {
            case InlineTokenStyle.Bold:
                span.FontAttributes = FontAttributes.Bold;
                break;
            case InlineTokenStyle.Italic:
                span.FontAttributes = FontAttributes.Italic;
                break;
            case InlineTokenStyle.Strikethrough:
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case InlineTokenStyle.FootnoteReference:
                span.FontSize = Math.Max(10, fontSize - 4);
                span.FontAttributes = FontAttributes.Bold;
                span.TextColor = Color.FromArgb("#8D5A2B");
                break;
        }

        return span;
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

    private Span CreateInlineCodeSpan(string text, double fontSize)
    {
        var span = CreateSpan(text, fontSize - 1);
        span.FontFamily = "Courier New";
        span.BackgroundColor = Color.FromArgb("#E8E1D3");
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

        if (double.TryParse(value, out _))
        {
            return "number";
        }

        return null;
    }

    private readonly record struct InlineToken(string Text, InlineTokenStyle Style, string? Target = null);
    private readonly record struct CodeToken(string Text, string? TokenType);
    private readonly record struct CodeCacheKey(string Code, string Language);

    private enum InlineTokenStyle
    {
        Plain,
        Bold,
        Italic,
        Strikethrough,
        InlineCode,
        Link,
        FootnoteReference
    }
}
