using System.Text.RegularExpressions;

namespace MauiMds.Controls;

internal sealed class MarkdownSyntaxHighlighter
{
    // DEPRECATED / UNIMPLEMENTED FOR ACTIVE USE:
    // This formatter is retained for a future editor-highlighting rewrite, but it is
    // intentionally disabled in the current editing experience because of performance issues.
    private static readonly Regex HeaderPattern = new(@"^(?<indent>\s{0,3})(?<marker>#{1,6})(?<space>\s+)(?<content>.*)$", RegexOptions.Compiled);
    private static readonly Regex BlockQuotePattern = new(@"^(?<indent>\s{0,3})(?<marker>>+)(?<space>\s*)(?<content>.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskPattern = new(@"^(?<indent>\s*)(?<marker>[-*]\s+\[[ xX]\])(?<space>\s+)(?<content>.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^(?<indent>\s*)(?<marker>[-*+])(?<space>\s+)(?<content>.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedPattern = new(@"^(?<indent>\s*)(?<marker>\d+\.) (?<content>.*)$", RegexOptions.Compiled);
    private static readonly Regex FrontMatterKeyPattern = new(@"^(?<key>[A-Za-z0-9_-]+)(?<colon>\s*:\s*)(?<value>.*)$", RegexOptions.Compiled);
    private static readonly Regex FencePattern = new(@"^(?<indent>\s*)(?<marker>`{3,}|~{3,})(?<lang>[A-Za-z0-9_-]*)\s*$", RegexOptions.Compiled);
    private static readonly Regex TokenPattern = new(@"(!\[[^\]]*\]\([^)]*\)|\[[^\]]+\]\([^)]*\)|\[[^\]]+\]\[[^\]]*\]|\[\^[^\]]+\]|`[^`]+`|\*\*[^*]+\*\*|~~[^~]+~~|==[^=]+=+|__[^_]+__|(?<!\*)\*[^*]+\*(?!\*)|\^[^\^]+\^|(?<!~)~[^~]+~(?!~)|https?://\S+)", RegexOptions.Compiled);
    private readonly Features.Markdown.MarkdownInlineFormatter _inlineFormatter = new();

    public FormattedString BuildFormattedText(string text)
    {
        var formatted = new FormattedString();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        var inFrontMatter = false;
        var inCodeFence = false;
        var codeLanguage = string.Empty;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (index == 0 && (trimmed == "---" || trimmed == "+++"))
            {
                inFrontMatter = true;
                AppendToken(formatted, line, SyntaxColor.FrontMatterDelimiter);
            }
            else if (inFrontMatter)
            {
                if (trimmed == "---" || trimmed == "+++")
                {
                    AppendToken(formatted, line, SyntaxColor.FrontMatterDelimiter);
                    inFrontMatter = false;
                }
                else
                {
                    AppendFrontMatterLine(formatted, line);
                }
            }
            else if (FencePattern.Match(line) is { Success: true } fenceMatch)
            {
                inCodeFence = !inCodeFence;
                codeLanguage = inCodeFence ? fenceMatch.Groups["lang"].Value : string.Empty;
                AppendToken(formatted, line, SyntaxColor.CodeFence);
            }
            else if (inCodeFence)
            {
                AppendCodeLine(formatted, line, codeLanguage);
            }
            else if (TryAppendStructuredLine(formatted, line))
            {
            }
            else
            {
                AppendInlineTokens(formatted, line);
            }

            if (index < lines.Length - 1)
            {
                formatted.Spans.Add(CreateSpan(Environment.NewLine, SyntaxColor.Plain));
            }
        }

        return formatted;
    }

    private static readonly Regex AdmonitionPattern = new(@"^>\s*\[!(NOTE|TIP|WARNING|IMPORTANT|CAUTION|INFO|DANGER|SUCCESS|BUG|EXAMPLE|QUESTION|ABSTRACT|TLDR)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DefinitionDetailPattern = new(@"^:\s+", RegexOptions.Compiled);

    private bool TryAppendStructuredLine(FormattedString formatted, string line)
    {
        // Admonition: > [!NOTE]
        var admonitionMatch = AdmonitionPattern.Match(line);
        if (admonitionMatch.Success)
        {
            AppendToken(formatted, admonitionMatch.Value, SyntaxColor.HeaderMarker);
            AppendInlineTokens(formatted, line[admonitionMatch.Length..]);
            return true;
        }

        // Definition detail: : term
        if (DefinitionDetailPattern.IsMatch(line))
        {
            AppendToken(formatted, ": ", SyntaxColor.ListMarker);
            AppendInlineTokens(formatted, line[2..]);
            return true;
        }

        if (AppendPatternMatch(formatted, HeaderPattern.Match(line), SyntaxColor.HeaderMarker))
        {
            return true;
        }

        if (AppendPatternMatch(formatted, BlockQuotePattern.Match(line), SyntaxColor.BlockQuoteMarker))
        {
            return true;
        }

        if (AppendPatternMatch(formatted, TaskPattern.Match(line), SyntaxColor.ListMarker))
        {
            return true;
        }

        if (AppendPatternMatch(formatted, BulletPattern.Match(line), SyntaxColor.ListMarker))
        {
            return true;
        }

        if (!OrderedPattern.Match(line).Success)
        {
            return false;
        }

        var orderedMatch = OrderedPattern.Match(line);
        AppendRaw(formatted, orderedMatch.Groups["indent"].Value);
        AppendToken(formatted, orderedMatch.Groups["marker"].Value.TrimEnd(), SyntaxColor.ListMarker);
        AppendRaw(formatted, " ");
        AppendInlineTokens(formatted, orderedMatch.Groups["content"].Value);
        return true;
    }

    private bool AppendPatternMatch(FormattedString formatted, Match match, SyntaxColor markerColor)
    {
        if (!match.Success)
        {
            return false;
        }

        AppendRaw(formatted, match.Groups["indent"].Value);
        AppendToken(formatted, match.Groups["marker"].Value, markerColor);
        AppendRaw(formatted, match.Groups["space"].Value);
        AppendInlineTokens(formatted, match.Groups["content"].Value);
        return true;
    }

    private void AppendFrontMatterLine(FormattedString formatted, string line)
    {
        var match = FrontMatterKeyPattern.Match(line);
        if (!match.Success)
        {
            AppendToken(formatted, line, SyntaxColor.FrontMatterValue);
            return;
        }

        AppendToken(formatted, match.Groups["key"].Value, SyntaxColor.FrontMatterKey);
        AppendToken(formatted, match.Groups["colon"].Value, SyntaxColor.FrontMatterDelimiter);
        AppendToken(formatted, match.Groups["value"].Value, SyntaxColor.FrontMatterValue);
    }

    private void AppendCodeLine(FormattedString formatted, string line, string language)
    {
        var codeText = _inlineFormatter.BuildCodeFormattedText(line, language);
        var spans = codeText.Spans.ToList();
        if (spans.Count > 0 && spans[^1].Text == Environment.NewLine)
        {
            spans.RemoveAt(spans.Count - 1);
        }

        foreach (var span in spans)
        {
            formatted.Spans.Add(span);
        }
    }

    private void AppendInlineTokens(FormattedString formatted, string line)
    {
        var index = 0;

        foreach (Match match in TokenPattern.Matches(line))
        {
            if (match.Index > index)
            {
                AppendRaw(formatted, line.Substring(index, match.Index - index));
            }

            AppendToken(formatted, match.Value, ClassifyToken(match.Value));
            index = match.Index + match.Length;
        }

        if (index < line.Length)
        {
            AppendRaw(formatted, line[index..]);
        }
    }

    private static SyntaxColor ClassifyToken(string token)
    {
        if (token.StartsWith("![", StringComparison.Ordinal))
        {
            return SyntaxColor.Image;
        }

        if (token.StartsWith("[^", StringComparison.Ordinal))
        {
            return SyntaxColor.Footnote;
        }

        if (token.StartsWith("[", StringComparison.Ordinal))
        {
            return SyntaxColor.Link;
        }

        if (token.StartsWith("`", StringComparison.Ordinal))
        {
            return SyntaxColor.InlineCode;
        }

        if (token.StartsWith("**", StringComparison.Ordinal) ||
            token.StartsWith("*", StringComparison.Ordinal) ||
            token.StartsWith("~~", StringComparison.Ordinal) ||
            token.StartsWith("==", StringComparison.Ordinal) ||
            token.StartsWith("__", StringComparison.Ordinal) ||
            token.StartsWith("^", StringComparison.Ordinal) ||
            (token.StartsWith("~", StringComparison.Ordinal) && !token.StartsWith("~~", StringComparison.Ordinal)))
        {
            return SyntaxColor.Emphasis;
        }

        if (token.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return SyntaxColor.Link;
        }

        return SyntaxColor.Plain;
    }

    private void AppendRaw(FormattedString formatted, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        formatted.Spans.Add(CreateSpan(text, SyntaxColor.Plain));
    }

    private void AppendToken(FormattedString formatted, string text, SyntaxColor color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        formatted.Spans.Add(CreateSpan(text, color));
    }

    private static Span CreateSpan(string text, SyntaxColor color)
    {
        var span = new Span
        {
            Text = text,
            FontFamily = "Courier New",
            FontSize = 15
        };

        switch (color)
        {
            case SyntaxColor.HeaderMarker:
                span.FontAttributes = FontAttributes.Bold;
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#7A3E9D"), Color.FromArgb("#C792EA"));
                break;
            case SyntaxColor.BlockQuoteMarker:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#7C6B58"), Color.FromArgb("#B9A98F"));
                break;
            case SyntaxColor.ListMarker:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#B35C1E"), Color.FromArgb("#F0A65E"));
                break;
            case SyntaxColor.Link:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#2B6CB0"), Color.FromArgb("#7DB6FF"));
                break;
            case SyntaxColor.Image:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#0F766E"), Color.FromArgb("#5EEAD4"));
                break;
            case SyntaxColor.InlineCode:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#8C4A16"), Color.FromArgb("#FFD08A"));
                span.BackgroundColor = Color.FromArgb("#2A000000");
                break;
            case SyntaxColor.Emphasis:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#A61E4D"), Color.FromArgb("#FF8FB1"));
                break;
            case SyntaxColor.Footnote:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#8D5A2B"), Color.FromArgb("#F2B880"));
                break;
            case SyntaxColor.CodeFence:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#6B7280"), Color.FromArgb("#9CA3AF"));
                break;
            case SyntaxColor.FrontMatterDelimiter:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#8B6F47"), Color.FromArgb("#E8C68A"));
                break;
            case SyntaxColor.FrontMatterKey:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#7C3AED"), Color.FromArgb("#C4B5FD"));
                break;
            case SyntaxColor.FrontMatterValue:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#0F766E"), Color.FromArgb("#99F6E4"));
                break;
            default:
                span.SetAppThemeColor(Span.TextColorProperty, Color.FromArgb("#111111"), Color.FromArgb("#F6F0E8"));
                break;
        }

        return span;
    }

    private enum SyntaxColor
    {
        Plain,
        HeaderMarker,
        BlockQuoteMarker,
        ListMarker,
        Link,
        Image,
        InlineCode,
        Emphasis,
        Footnote,
        CodeFence,
        FrontMatterDelimiter,
        FrontMatterKey,
        FrontMatterValue
    }
}
